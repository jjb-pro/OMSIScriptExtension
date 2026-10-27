using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using OMSIScript.Analysis;
using OMSIScript.Features;
using OMSIScript.LanguageServer.JsonRpc;
using OMSIScript.LanguageServer.Protocol;
using OMSIScript.Project;
using OMSIScript.Text;
using CoreFormattingOptions = OMSIScript.Features.FormattingOptions;
using CoreLocation = OMSIScript.Features.Location;
using Location = OMSIScript.LanguageServer.Protocol.Location;
using Range = OMSIScript.LanguageServer.Protocol.Range;

namespace OMSIScript.LanguageServer;

public sealed class Server
{
    private const int MethodNotFound = -32601;
    private const int InternalError = -32603;
    private const int RequestFailed = -32803;
    private const int ServerNotInitialized = -32002;

    private static ProtocolJsonContext Json => ProtocolJsonContext.Default;

    private readonly MessageReader _reader;
    private readonly MessageWriter _writer;
    private readonly DocumentStore _documents = new();
    private readonly Workspace _workspace;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, (long Stamp, IReadOnlyList<ContextAnalysis> Analyses)> _analysisCache = new(PathComparer.Instance);
    private readonly HashSet<string> _publishedProjectFiles = new(PathComparer.Instance);
    private readonly CancellationTokenSource _shutdown = new();
    private ServerSettings _settings = new();
    private Timer? _diagnosticsTimer;
    private long _stamp;
    private bool _initialized;
    private bool _shutdownRequested;
    private int _indexing;

    public Server(Stream input, Stream output)
    {
        _reader = new MessageReader(input);
        _writer = new MessageWriter(output);
        _workspace = new Workspace(_documents);
        _workspace.IndexChanged += () => Enqueue(() =>
        {
            Invalidate();
            PublishAll();
            return Task.CompletedTask;
        });
    }

    public int ExitCode { get; private set; } = 1;

    public async Task RunAsync()
    {
        var consumer = Task.Run(ConsumeAsync);
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var message = await _reader.ReadAsync(_shutdown.Token);
                if (message is null)
                    break;

                await _queue.Writer.WriteAsync(() => HandleAsync(message));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }

        _queue.Writer.TryComplete();
        await consumer;
    }

    private async Task ConsumeAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await work();
            }
            catch (Exception exception)
            {
                Log(1, $"Unhandled error: {exception}");
            }
        }
    }

    private void Enqueue(Func<Task> work) => _queue.Writer.TryWrite(work);

    private async Task HandleAsync(IncomingMessage message)
    {
        using (message.Document)
        {
            if (message.IsResponse || message.Method is null)
                return;

            var id = message.Id;
            try
            {
                if (!_initialized && message.Method is not ("initialize" or "exit"))
                {
                    if (id is { } pending)
                        _writer.WriteError(pending, ServerNotInitialized, "The server is not initialized.");
                    return;
                }

                await DispatchAsync(message.Method, id, message.Params);
            }
            catch (Exception exception) when (id is not null)
            {
                Log(1, $"{message.Method} failed: {exception}");
                _writer.WriteError(id, InternalError, exception.Message);
            }
        }
    }

    private Task DispatchAsync(string method, JsonElement? id, JsonElement? parameters)
    {
        switch (method)
        {
            case "initialize":
                Respond(id, Initialize(Parse(parameters, Json.InitializeParams)), Json.InitializeResult);
                break;
            case "initialized":
                StartIndexing();
                break;
            case "shutdown":
                _shutdownRequested = true;
                _writer.WriteNullResponse(id!.Value);
                break;
            case "exit":
                ExitCode = _shutdownRequested ? 0 : 1;
                _shutdown.Cancel();
                Environment.Exit(ExitCode);
                break;

            case "textDocument/didOpen":
                DidOpen(Parse(parameters, Json.DidOpenTextDocumentParams));
                break;
            case "textDocument/didChange":
                DidChange(Parse(parameters, Json.DidChangeTextDocumentParams));
                break;
            case "textDocument/didClose":
                DidClose(Parse(parameters, Json.DidCloseTextDocumentParams));
                break;
            case "textDocument/didSave":
                break;

            case "textDocument/hover":
                RespondNullable(id, Hover(Parse(parameters, Json.TextDocumentPositionParams)), Json.Hover);
                break;
            case "textDocument/completion":
                Respond(id, Completion(Parse(parameters, Json.TextDocumentPositionParams)), Json.CompletionList);
                break;
            case "textDocument/definition":
                Respond(id, Definition(Parse(parameters, Json.TextDocumentPositionParams)), Json.ListLocation);
                break;
            case "textDocument/references":
                Respond(id, References(Parse(parameters, Json.ReferenceParams)), Json.ListLocation);
                break;
            case "textDocument/documentHighlight":
                Respond(id, Highlights(Parse(parameters, Json.TextDocumentPositionParams)), Json.ListDocumentHighlight);
                break;
            case "textDocument/documentSymbol":
                Respond(id, DocumentSymbols(Parse(parameters, Json.DocumentSymbolParams)), Json.ListDocumentSymbol);
                break;
            case "textDocument/foldingRange":
                Respond(id, Folding(Parse(parameters, Json.FoldingRangeParams)), Json.ListFoldingRange);
                break;
            case "textDocument/formatting":
                {
                    var p = Parse(parameters, Json.DocumentFormattingParams);
                    Respond(id, Format(p.TextDocument, p.Options, null), Json.ListTextEdit);
                    break;
                }
            case "textDocument/rangeFormatting":
                {
                    var p = Parse(parameters, Json.DocumentRangeFormattingParams);
                    Respond(id, Format(p.TextDocument, p.Options, p.Range), Json.ListTextEdit);
                    break;
                }
            case "textDocument/codeAction":
                Respond(id, CodeActions(Parse(parameters, Json.CodeActionParams)), Json.ListCodeAction);
                break;
            case "textDocument/prepareRename":
                PrepareRename(id, Parse(parameters, Json.TextDocumentPositionParams));
                break;
            case "textDocument/rename":
                Rename(id, Parse(parameters, Json.RenameParams));
                break;
            case "textDocument/inlayHint":
                Respond(id, InlayHints(Parse(parameters, Json.InlayHintParams)), Json.ListInlayHint);
                break;

            case "workspace/symbol":
                Respond(id, WorkspaceSymbols(Parse(parameters, Json.WorkspaceSymbolParams)), Json.ListSymbolInformation);
                break;
            case "workspace/didChangeWatchedFiles":
                DidChangeWatchedFiles(Parse(parameters, Json.DidChangeWatchedFilesParams));
                break;
            case "workspace/didChangeConfiguration":
                if (Parse(parameters, Json.DidChangeConfigurationParams).Settings?.OmsiScript is { } settings)
                    ApplySettings(settings);
                break;
            case "workspace/didChangeWorkspaceFolders":
                foreach (var folder in Parse(parameters, Json.DidChangeWorkspaceFoldersParams).Event.Added)
                    _workspace.AddRoot(Uris.ToPath(folder.Uri));
                StartIndexing();
                break;

            case "omsiScript/scriptContext":
                Respond(id, ScriptContext(Parse(parameters, Json.TextDocumentPositionParams)), Json.ScriptContextResult);
                break;

            default:
                if (id is { } requestId && !method.StartsWith("$/", StringComparison.Ordinal))
                    _writer.WriteError(requestId, MethodNotFound, $"Unsupported method '{method}'.");
                break;
        }

        return Task.CompletedTask;
    }

    private static T Parse<T>(JsonElement? parameters, JsonTypeInfo<T> typeInfo)
        => parameters is { } value ? value.Deserialize(typeInfo) ?? throw new InvalidDataException("Missing parameters.") : throw new InvalidDataException("Missing parameters.");

    private void Respond<T>(JsonElement? id, T result, JsonTypeInfo<T> typeInfo)
    {
        if (id is { } value)
            _writer.WriteResponse(value, result, typeInfo);
    }

    private void RespondNullable<T>(JsonElement? id, T? result, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (id is not { } value)
            return;
        if (result is null)
            _writer.WriteNullResponse(value);
        else
            _writer.WriteResponse(value, result, typeInfo);
    }

    private void Log(int type, string message)
    {
        Console.Error.WriteLine(message);
        _writer.WriteNotification("window/logMessage", new LogMessageParams(type, message), Json.LogMessageParams);
    }

    private InitializeResult Initialize(InitializeParams parameters)
    {
        _initialized = true;
        if (parameters.InitializationOptions is { } settings)
            ApplySettings(settings);

        if (parameters.WorkspaceFolders is { Count: > 0 } folders)
        {
            foreach (var folder in folders)
                _workspace.AddRoot(Uris.ToPath(folder.Uri));
        }
        else if (parameters.RootUri is { } root)
        {
            _workspace.AddRoot(Uris.ToPath(root));
        }

        var version = typeof(Server).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return new InitializeResult(new ServerCapabilities
        {
            TextDocumentSync = new TextDocumentSyncOptions(true, 2, new SaveOptions(false)),
            HoverProvider = true,
            CompletionProvider = new CompletionOptions(["(", ".", "{", "$", ":"], false),
            DefinitionProvider = true,
            ReferencesProvider = true,
            DocumentHighlightProvider = true,
            DocumentSymbolProvider = true,
            WorkspaceSymbolProvider = true,
            CodeActionProvider = new CodeActionOptions(["quickfix"]),
            DocumentFormattingProvider = true,
            DocumentRangeFormattingProvider = true,
            RenameProvider = new RenameOptions(true),
            FoldingRangeProvider = true,
            InlayHintProvider = true,
            Workspace = new WorkspaceServerCapabilities(new WorkspaceFoldersServerCapabilities(true, true)),
        }, new ServerInfo("OMSI Script Language Server", version.Split('+')[0]));
    }

    private void ApplySettings(ServerSettings settings)
    {
        _settings = settings;
        _workspace.GamePathOverride = settings.GamePath;
        Invalidate();
        PublishAll();
    }

    private void StartIndexing()
    {
        if (_workspace.PendingRoots().Count == 0 || Interlocked.Exchange(ref _indexing, 1) == 1)
            return;

        Task.Run(() =>
        {
            try
            {
                _workspace.IndexPendingRoots(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    Log(1, $"Indexing failed: {exception.Message}");
                    return Task.CompletedTask;
                });
            }
            finally
            {
                Interlocked.Exchange(ref _indexing, 0);
                if (_workspace.PendingRoots().Count > 0)
                    Enqueue(() =>
                    {
                        StartIndexing();
                        return Task.CompletedTask;
                    });
            }
        });
    }

    private void Invalidate()
    {
        _stamp++;
        _analysisCache.Clear();
    }

    private IReadOnlyList<ContextAnalysis> Analyses(string path)
    {
        if (_analysisCache.TryGetValue(path, out var cached) && cached.Stamp == _stamp)
            return cached.Analyses;

        var analyses = _workspace.Analyze(path);
        _analysisCache[path] = (_stamp, analyses);
        return analyses;
    }

    private FeatureContext Context(string path) => new(_workspace, path, Analyses(path));

    private SourceText Source(FeatureContext context, string path) => context.GetSource(path) ?? new SourceText(string.Empty);

    private void DidOpen(DidOpenTextDocumentParams parameters)
    {
        var document = _documents.Open(parameters.TextDocument.Uri, parameters.TextDocument.Version, parameters.TextDocument.Text);
        Invalidate();
        if (Path.GetExtension(document.Path).Equals(".osc", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.EnsureRootFor(document.Path);
            StartIndexing();
        }
        else
        {
            _workspace.OnFileChanged(document.Path, deleted: false);
        }

        PublishAll();
    }

    private void DidChange(DidChangeTextDocumentParams parameters)
    {
        if (_documents.Change(parameters.TextDocument.Uri, parameters.TextDocument.Version, parameters.ContentChanges) is not { } document)
            return;

        Invalidate();
        if (_workspace.GetObject(document.Path) is not null)
            _workspace.OnFileChanged(document.Path, deleted: false);

        _diagnosticsTimer ??= new Timer(_ => Enqueue(() =>
        {
            PublishAll();
            return Task.CompletedTask;
        }));
        _diagnosticsTimer.Change(250, Timeout.Infinite);
    }

    private void DidClose(DidCloseTextDocumentParams parameters)
    {
        var path = Uris.ToPath(parameters.TextDocument.Uri);
        _documents.Close(parameters.TextDocument.Uri);
        _workspace.OnFileChanged(path, deleted: false);
        Invalidate();
        _writer.WriteNotification("textDocument/publishDiagnostics", new PublishDiagnosticsParams(parameters.TextDocument.Uri, []), Json.PublishDiagnosticsParams);
        PublishAll();
    }

    private void DidChangeWatchedFiles(DidChangeWatchedFilesParams parameters)
    {
        foreach (var change in parameters.Changes)
        {
            var path = Uris.ToPath(change.Uri);
            if (_documents.Get(path) is null)
                _workspace.OnFileChanged(path, change.Type == 3);
        }

        Invalidate();
        PublishAll();
    }

    private void PublishAll()
    {
        var projectDiagnostics = new Dictionary<string, List<ScriptDiagnostic>>(PathComparer.Instance);
        var seenOwners = new HashSet<string>(PathComparer.Instance);

        foreach (var document in _documents.All)
        {
            if (!Path.GetExtension(document.Path).Equals(".osc", StringComparison.OrdinalIgnoreCase))
                continue;

            var analyses = Analyses(document.Path);
            var diagnostics = Merge(document.Path, analyses);
            _writer.WriteNotification("textDocument/publishDiagnostics",
                new PublishDiagnosticsParams(document.Uri, [.. diagnostics.Select(d => ToDiagnostic(document.Text, d))], document.Version),
                Json.PublishDiagnosticsParams);

            foreach (var analysis in analyses)
            {
                if (analysis.Context.Owner is not { } owner || !seenOwners.Add(owner.Path))
                    continue;

                foreach (var diagnostic in ProjectDiagnostics.Compute(_workspace, analysis.Context))
                {
                    if (!projectDiagnostics.TryGetValue(diagnostic.Path, out var list))
                        projectDiagnostics[diagnostic.Path] = list = [];
                    list.Add(diagnostic);
                }
            }
        }

        foreach (var stale in _publishedProjectFiles.Where(p => !projectDiagnostics.ContainsKey(p)).ToList())
        {
            _writer.WriteNotification("textDocument/publishDiagnostics", new PublishDiagnosticsParams(_documents.UriFor(stale), []), Json.PublishDiagnosticsParams);
            _publishedProjectFiles.Remove(stale);
        }

        foreach (var (path, list) in projectDiagnostics)
        {
            var text = _workspace.ReadText(path, detectUtf8: true);
            if (text is null)
                continue;

            var source = new SourceText(text);
            var converted = ApplySeverity(list.DistinctBy(d => (d.Span, d.Code, d.Message))).Select(d => ToDiagnostic(source, d)).ToList();
            _writer.WriteNotification("textDocument/publishDiagnostics", new PublishDiagnosticsParams(_documents.UriFor(path), converted), Json.PublishDiagnosticsParams);
            _publishedProjectFiles.Add(path);
        }
    }

    private List<ScriptDiagnostic> Merge(string path, IReadOnlyList<ContextAnalysis> analyses)
    {
        var groups = analyses
            .SelectMany(a => a.DiagnosticsFor(path).Select(d => (Diagnostic: d, Owner: a.Context.DisplayName)))
            .GroupBy(x => (x.Diagnostic.Span, x.Diagnostic.Code, x.Diagnostic.Message));

        var merged = new List<ScriptDiagnostic>();
        foreach (var group in groups)
        {
            var owners = group.Select(g => g.Owner).Distinct().ToList();
            var diagnostic = group.First().Diagnostic;
            if (analyses.Count > 1 && owners.Count < analyses.Count)
                diagnostic = diagnostic with { Message = $"{diagnostic.Message} [{string.Join(", ", owners)}]" };
            merged.Add(diagnostic);
        }

        return [.. ApplySeverity(merged)];
    }

    private IEnumerable<ScriptDiagnostic> ApplySeverity(IEnumerable<ScriptDiagnostic> diagnostics)
    {
        var overrides = _settings.Diagnostics?.Severity;
        foreach (var diagnostic in diagnostics)
        {
            if (overrides is null || !overrides.TryGetValue(diagnostic.Code, out var configured))
            {
                yield return diagnostic;
                continue;
            }

            Severity? severity = configured.ToLowerInvariant() switch
            {
                "error" => Severity.Error,
                "warning" => Severity.Warning,
                "information" or "info" => Severity.Information,
                "hint" => Severity.Hint,
                "none" or "off" => null,
                _ => diagnostic.Severity,
            };

            if (severity is { } value)
                yield return diagnostic with { Severity = value };
        }
    }

    private static Protocol.Diagnostic ToDiagnostic(SourceText source, ScriptDiagnostic diagnostic) => new()
    {
        Range = Conversions.ToRange(source, diagnostic.Span),
        Severity = (int)diagnostic.Severity,
        Code = diagnostic.Code,
        Message = diagnostic.Message,
        Tags = diagnostic.Tag == DiagnosticTag.None ? null : [(int)diagnostic.Tag],
    };

    private Hover? Hover(TextDocumentPositionParams parameters)
    {
        var path = Uris.ToPath(parameters.TextDocument.Uri);
        var context = Context(path);
        var offset = Conversions.ToOffset(context.Document.Source, parameters.Position);
        var hover = HoverProvider.GetHover(context, offset);
        return hover is null ? null : new Hover(MarkupContent.Markdown(hover.Markdown), Conversions.ToRange(context.Document.Source, hover.Span));
    }

    private CompletionList Completion(TextDocumentPositionParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var source = context.Document.Source;
        var offset = Conversions.ToOffset(source, parameters.Position);
        var items = CompletionProvider.GetCompletions(context, offset).Select(entry => new CompletionItem
        {
            Label = entry.Label,
            Kind = entry.Kind switch
            {
                CompletionKind.Keyword => 14,
                CompletionKind.Snippet => 15,
                CompletionKind.Operator => 24,
                CompletionKind.Variable => 6,
                CompletionKind.Function => 3,
                CompletionKind.Constant => 21,
                CompletionKind.Event => 23,
                CompletionKind.Module => 9,
                _ => 12,
            },
            Detail = entry.Detail,
            Documentation = entry.Documentation is null ? null : MarkupContent.Markdown(entry.Documentation),
            SortText = entry.SortText,
            FilterText = entry.FilterText,
            InsertTextFormat = entry.IsSnippet ? 2 : 1,
            TextEdit = new TextEdit(Conversions.ToRange(source, entry.ReplaceSpan), entry.InsertText),
            Command = entry.Retrigger ? new Protocol.Command("Suggest", "editor.action.triggerSuggest") : null,
        }).ToList();

        return new CompletionList(false, items);
    }

    private List<Location> ToLocations(FeatureContext context, IEnumerable<CoreLocation> locations)
        => [.. locations.Select(l => new Location(_documents.UriFor(l.Path), Conversions.ToRange(l.Source ?? Source(context, l.Path), l.Span)))];

    private List<Location> Definition(TextDocumentPositionParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        return ToLocations(context, NavigationProvider.GetDefinitions(context, Conversions.ToOffset(context.Document.Source, parameters.Position)));
    }

    private List<Location> References(ReferenceParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var offset = Conversions.ToOffset(context.Document.Source, parameters.Position);
        return ToLocations(context, NavigationProvider.GetReferences(context, offset, parameters.Context.IncludeDeclaration));
    }

    private List<DocumentHighlight> Highlights(TextDocumentPositionParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var source = context.Document.Source;
        return [.. NavigationProvider.GetHighlights(context, Conversions.ToOffset(source, parameters.Position))
            .Select(h => new DocumentHighlight(Conversions.ToRange(source, h.Span), h.IsWrite ? 3 : 2))];
    }

    private List<DocumentSymbol> DocumentSymbols(DocumentSymbolParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var source = context.Document.Source;
        return [.. StructureProvider.GetOutline(context).Select(o => new DocumentSymbol(
            o.Name, o.Detail,
            o.Kind switch
            {
                BlockKind.Macro => 12,
                BlockKind.Trigger => 24,
                _ => 6,
            },
            Conversions.ToRange(source, o.Span),
            Conversions.ToRange(source, o.SelectionSpan)))];
    }

    private List<FoldingRange> Folding(FoldingRangeParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        return [.. StructureProvider.GetFolding(context).Select(f => new FoldingRange(f.StartLine, f.EndLine, f.Kind == FoldKind.Comment ? "comment" : "region"))];
    }

    private List<TextEdit> Format(TextDocumentIdentifier document, Protocol.FormattingOptions options, Range? range)
    {
        var path = Uris.ToPath(document.Uri);
        var script = _workspace.GetDocument(path);
        var changes = Formatter.Format(script, new CoreFormattingOptions(options.InsertSpaces, options.TabSize, options.TrimTrailingWhitespace ?? true),
            range?.Start.Line, range?.End.Line);
        return [.. changes.Select(c => new TextEdit(Conversions.ToRange(script.Source, c.Span), c.NewText))];
    }

    private List<CodeAction> CodeActions(CodeActionParams parameters)
    {
        var path = Uris.ToPath(parameters.TextDocument.Uri);
        var context = Context(path);
        var source = context.Document.Source;
        var span = source.GetSpan(Conversions.ToLineRange(parameters.Range));
        var diagnostics = Merge(path, context.Analyses).Where(d => d.Span.Start <= span.End && d.Span.End >= span.Start).ToList();

        return [.. CodeActionProvider.GetActions(context, diagnostics).Select(action => new CodeAction(
            action.Title,
            "quickfix",
            [ToDiagnostic(source, action.Diagnostic)],
            action.IsPreferred ? true : null,
            ToWorkspaceEdit(context, action.Changes)))];
    }

    private WorkspaceEdit ToWorkspaceEdit(FeatureContext context, IEnumerable<Features.TextChange> changes)
    {
        var edits = new Dictionary<string, List<TextEdit>>();
        foreach (var group in changes.GroupBy(c => PathComparer.Normalize(c.Path)))
        {
            var path = group.First().Path;
            var source = Source(context, path);
            edits[_documents.UriFor(path)] = [.. group.Select(c => new TextEdit(Conversions.ToRange(source, c.Span), c.NewText))];
        }

        return new WorkspaceEdit(edits);
    }

    private void PrepareRename(JsonElement? id, TextDocumentPositionParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var source = context.Document.Source;
        var result = NavigationProvider.PrepareRename(context, Conversions.ToOffset(source, parameters.Position), out var error);
        if (result is { } value)
            Respond(id, new PrepareRenameResult(Conversions.ToRange(source, value.Span), value.Name), Json.PrepareRenameResult);
        else if (id is { } requestId)
            _writer.WriteError(requestId, RequestFailed, error ?? "The element cannot be renamed.");
    }

    private void Rename(JsonElement? id, RenameParams parameters)
    {
        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var changes = NavigationProvider.Rename(context, Conversions.ToOffset(context.Document.Source, parameters.Position), parameters.NewName, out var error);
        if (changes is not null)
            Respond(id, ToWorkspaceEdit(context, changes), Json.WorkspaceEdit);
        else if (id is { } requestId)
            _writer.WriteError(requestId, RequestFailed, error ?? "The element cannot be renamed.");
    }

    private List<InlayHint> InlayHints(InlayHintParams parameters)
    {
        if (_settings.InlayHints is { Constants: false })
            return [];

        var context = Context(Uris.ToPath(parameters.TextDocument.Uri));
        var source = context.Document.Source;
        var span = source.GetSpan(Conversions.ToLineRange(parameters.Range));
        return [.. InlayHintProvider.GetHints(context, span.Start, span.End)
            .Select(h => new InlayHint(Conversions.ToPosition(source.GetPosition(h.Offset)), h.Label, 1, h.Tooltip, true))];
    }

    private List<SymbolInformation> WorkspaceSymbols(WorkspaceSymbolParams parameters)
    {
        var query = parameters.Query;
        var results = new List<SymbolInformation>();
        var seen = new HashSet<(string, int)>();
        var paths = _documents.All.Select(d => d.Path)
            .Concat(_workspace.Objects.SelectMany(o => o.Scripts.Select(s => s.FullPath)))
            .Where(p => Path.GetExtension(p).Equals(".osc", StringComparison.OrdinalIgnoreCase))
            .Distinct(PathComparer.Instance)
            .Take(5000);

        foreach (var path in paths)
        {
            if (!_workspace.Exists(path))
                continue;

            var document = _workspace.GetDocument(path);
            foreach (var command in document.Commands)
            {
                if (command.Kind is not (Syntax.CommandKind.MacroHeader or Syntax.CommandKind.TriggerHeader))
                    continue;
                if (query.Length > 0 && !command.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add((PathComparer.Normalize(path)!, command.Span.Start)))
                    continue;

                results.Add(new SymbolInformation(command.Name, command.Kind == Syntax.CommandKind.MacroHeader ? 12 : 24,
                    new Location(_documents.UriFor(path), Conversions.ToRange(document.Source, command.NameSpan)), Path.GetFileName(path)));
                if (results.Count >= 1000)
                    return results;
            }
        }

        return results;
    }

    private ScriptContextResult ScriptContext(TextDocumentPositionParams parameters)
    {
        var path = Uris.ToPath(parameters.TextDocument.Uri);
        var owners = _workspace.GetOwners(path).Select(owner =>
        {
            var scripts = owner.Scripts.Select(s => s.FullPath).ToList();
            var position = scripts.FindIndex(s => PathComparer.Instance.Equals(s, path));
            return new ScriptContextInfo(owner.Path, owner.Kind.ToString(), scripts, position);
        }).ToList();

        return new ScriptContextResult(owners, _workspace.GetGameRoot(path));
    }
}
