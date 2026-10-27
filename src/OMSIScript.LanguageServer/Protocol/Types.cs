using System.Text.Json;
using System.Text.Json.Serialization;

namespace OMSIScript.LanguageServer.Protocol;

public sealed record Position(int Line, int Character);

public sealed record Range(Position Start, Position End);

public sealed record Location(string Uri, Range Range);

public sealed record TextDocumentIdentifier(string Uri);

public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

public sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);

public sealed record TextDocumentContentChangeEvent(Range? Range, string Text);

public sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, IReadOnlyList<TextDocumentContentChangeEvent> ContentChanges);

public sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);

public sealed record DidSaveTextDocumentParams(TextDocumentIdentifier TextDocument);

public sealed record TextDocumentPositionParams(TextDocumentIdentifier TextDocument, Position Position);

public sealed record ReferenceContext(bool IncludeDeclaration);

public sealed record ReferenceParams(TextDocumentIdentifier TextDocument, Position Position, ReferenceContext Context);

public sealed record RenameParams(TextDocumentIdentifier TextDocument, Position Position, string NewName);

public sealed record PrepareRenameResult(Range Range, string Placeholder);

public sealed record MarkupContent(string Kind, string Value)
{
    public static MarkupContent Markdown(string value) => new("markdown", value);
}

public sealed record Hover(MarkupContent Contents, Range? Range);

public sealed record Command(string Title, [property: JsonPropertyName("command")] string Name, IReadOnlyList<JsonElement>? Arguments = null);

public sealed record TextEdit(Range Range, string NewText);

public sealed record CompletionItem
{
    public required string Label { get; init; }
    public int? Kind { get; init; }
    public string? Detail { get; init; }
    public MarkupContent? Documentation { get; init; }
    public string? SortText { get; init; }
    public string? FilterText { get; init; }
    public int? InsertTextFormat { get; init; }
    public TextEdit? TextEdit { get; init; }
    public Command? Command { get; init; }
}

public sealed record CompletionList(bool IsIncomplete, IReadOnlyList<CompletionItem> Items);

public sealed record CodeDescription(string Href);

public sealed record Diagnostic
{
    public required Range Range { get; init; }
    public int Severity { get; init; }
    public string? Code { get; init; }
    public CodeDescription? CodeDescription { get; init; }
    public string Source { get; init; } = "omsi-script";
    public required string Message { get; init; }
    public IReadOnlyList<int>? Tags { get; init; }
    public JsonElement? Data { get; init; }
}

public sealed record PublishDiagnosticsParams(string Uri, IReadOnlyList<Diagnostic> Diagnostics, int? Version = null);

public sealed record DocumentSymbol(string Name, string? Detail, int Kind, Range Range, Range SelectionRange);

public sealed record SymbolInformation(string Name, int Kind, Location Location, string? ContainerName);

public sealed record WorkspaceSymbolParams(string Query);

public sealed record DocumentSymbolParams(TextDocumentIdentifier TextDocument);

public sealed record FoldingRangeParams(TextDocumentIdentifier TextDocument);

public sealed record FoldingRange(int StartLine, int EndLine, string? Kind);

public sealed record DocumentHighlight(Range Range, int Kind);

public sealed record FormattingOptions(int TabSize, bool InsertSpaces, bool? TrimTrailingWhitespace = null);

public sealed record DocumentFormattingParams(TextDocumentIdentifier TextDocument, FormattingOptions Options);

public sealed record DocumentRangeFormattingParams(TextDocumentIdentifier TextDocument, Range Range, FormattingOptions Options);

public sealed record WorkspaceEdit(Dictionary<string, List<TextEdit>> Changes);

public sealed record CodeActionContext(IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string>? Only = null);

public sealed record CodeActionParams(TextDocumentIdentifier TextDocument, Range Range, CodeActionContext Context);

public sealed record CodeAction(string Title, string Kind, IReadOnlyList<Diagnostic>? Diagnostics, bool? IsPreferred, WorkspaceEdit? Edit);

public sealed record InlayHintParams(TextDocumentIdentifier TextDocument, Range Range);

public sealed record InlayHint(Position Position, string Label, int? Kind, string? Tooltip, bool? PaddingLeft);

public sealed record FileEvent(string Uri, int Type);

public sealed record DidChangeWatchedFilesParams(IReadOnlyList<FileEvent> Changes);

public sealed record WorkspaceFolder(string Uri, string Name);

public sealed record WorkspaceFoldersChangeEvent(IReadOnlyList<WorkspaceFolder> Added, IReadOnlyList<WorkspaceFolder> Removed);

public sealed record DidChangeWorkspaceFoldersParams(WorkspaceFoldersChangeEvent Event);

public sealed record DiagnosticSettings
{
    public Dictionary<string, string>? Severity { get; init; }
}

public sealed record InlayHintSettings
{
    public bool Constants { get; init; } = true;
}

public sealed record ServerSettings
{
    public string? GamePath { get; init; }
    public DiagnosticSettings? Diagnostics { get; init; }
    public InlayHintSettings? InlayHints { get; init; }
}

public sealed record ConfigurationSection([property: JsonPropertyName("omsiScript")] ServerSettings? OmsiScript);

public sealed record DidChangeConfigurationParams(ConfigurationSection? Settings);

public sealed record InitializeParams
{
    public int? ProcessId { get; init; }
    public string? RootUri { get; init; }
    public IReadOnlyList<WorkspaceFolder>? WorkspaceFolders { get; init; }
    public ServerSettings? InitializationOptions { get; init; }
}

public sealed record ServerInfo(string Name, string Version);

public sealed record SaveOptions(bool IncludeText);

public sealed record TextDocumentSyncOptions(bool OpenClose, int Change, SaveOptions Save);

public sealed record CompletionOptions(IReadOnlyList<string> TriggerCharacters, bool ResolveProvider);

public sealed record CodeActionOptions(IReadOnlyList<string> CodeActionKinds);

public sealed record RenameOptions(bool PrepareProvider);

public sealed record WorkspaceFoldersServerCapabilities(bool Supported, bool ChangeNotifications);

public sealed record WorkspaceServerCapabilities(WorkspaceFoldersServerCapabilities WorkspaceFolders);

public sealed record ServerCapabilities
{
    public required TextDocumentSyncOptions TextDocumentSync { get; init; }
    public bool HoverProvider { get; init; }
    public CompletionOptions? CompletionProvider { get; init; }
    public bool DefinitionProvider { get; init; }
    public bool ReferencesProvider { get; init; }
    public bool DocumentHighlightProvider { get; init; }
    public bool DocumentSymbolProvider { get; init; }
    public bool WorkspaceSymbolProvider { get; init; }
    public CodeActionOptions? CodeActionProvider { get; init; }
    public bool DocumentFormattingProvider { get; init; }
    public bool DocumentRangeFormattingProvider { get; init; }
    public RenameOptions? RenameProvider { get; init; }
    public bool FoldingRangeProvider { get; init; }
    public bool InlayHintProvider { get; init; }
    public WorkspaceServerCapabilities? Workspace { get; init; }
}

public sealed record InitializeResult(ServerCapabilities Capabilities, ServerInfo ServerInfo);

public sealed record LogMessageParams(int Type, string Message);

public sealed record ScriptContextInfo(string Owner, string Kind, IReadOnlyList<string> Scripts, int Position);

public sealed record ScriptContextResult(IReadOnlyList<ScriptContextInfo> Owners, string? GameRoot);

public sealed record ResponseError(int Code, string Message);
