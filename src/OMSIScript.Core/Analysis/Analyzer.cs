using System.Globalization;
using OMSIScript.Language;
using OMSIScript.Project;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Analysis;

public static class Analyzer
{
    public static ContextAnalysis Analyze(ScriptContext context) => new Run(context).Execute();

    private readonly record struct StreamItem(int Document, int Command);

    private sealed class Run(ScriptContext context)
    {
        private readonly List<ScriptDiagnostic> _diagnostics = [];
        private readonly List<SymbolOccurrence> _occurrences = [];
        private readonly List<StreamItem> _stream = [];
        private readonly List<MacroDefinition> _macros = [];
        private readonly List<MacroDefinition> _triggers = [];
        private readonly Dictionary<(string, int), MacroDefinition> _resolutions = [];
        private readonly Dictionary<string, List<BlockInfo>> _blocks = new(PathComparer.Instance);
        private readonly Dictionary<string, List<ConditionInfo>> _conditions = new(PathComparer.Instance);
        private int[] _blockOf = [];

        private IReadOnlyList<ScriptDocument> Documents => context.Scripts;

        private ScriptDocument Doc(StreamItem item) => Documents[item.Document];

        private Command Cmd(StreamItem item) => Documents[item.Document].Commands[item.Command];

        private ScriptDocument DocumentOf(string path) => Documents.First(d => PathComparer.Instance.Equals(d.Path, path));

        public ContextAnalysis Execute()
        {
            for (var d = 0; d < Documents.Count; d++)
            {
                _blocks[Documents[d].Path] = [];
                _conditions[Documents[d].Path] = [];
                for (var c = 0; c < Documents[d].Commands.Count; c++)
                    _stream.Add(new StreamItem(d, c));
            }

            _blockOf = new int[_stream.Count];

            foreach (var document in Documents)
                AnalyzeLexical(document);

            AnalyzeStructure();
            AnalyzeConditions();
            AnalyzeSymbols();
            AnalyzeRegisters();
            AnalyzeOperators();

            var files = new Dictionary<string, FileStructure>(PathComparer.Instance);
            foreach (var document in Documents)
                files[document.Path] = new FileStructure(document, _blocks[document.Path], _conditions[document.Path]);

            return new ContextAnalysis(context, _diagnostics, _occurrences, _macros, _triggers, files, _resolutions);
        }

        private void Report(ScriptDocument document, TextSpan span, DiagnosticDescriptor descriptor, params object[] args)
            => _diagnostics.Add(new ScriptDiagnostic(document.Path, span, descriptor.DefaultSeverity, descriptor, descriptor.Format(args)));

        private void ReportFix(ScriptDocument document, TextSpan span, DiagnosticDescriptor descriptor, string fix, params object[] args)
            => _diagnostics.Add(new ScriptDiagnostic(document.Path, span, descriptor.DefaultSeverity, descriptor, descriptor.Format(args), fix));

        private void ReportWithSeverity(ScriptDocument document, TextSpan span, DiagnosticDescriptor descriptor, Severity severity, params object[] args)
            => _diagnostics.Add(new ScriptDiagnostic(document.Path, span, severity, descriptor, descriptor.Format(args)));

        private static string Location(ScriptDocument document, TextSpan span)
            => $"{Path.GetFileName(document.Path)}:{document.Source.GetPosition(span.Start).Line + 1}";

        private string Owner => context.Owner is null ? "this script" : $"'{context.DisplayName}'";

        private void AnalyzeLexical(ScriptDocument document)
        {
            if (document.Lex.UnterminatedString is { } open)
                Report(document, new TextSpan(open.Start, 1), DiagnosticCodes.UnterminatedString);

            foreach (var command in document.Commands)
            {
                switch (command.Kind)
                {
                    case CommandKind.Invalid:
                        ReportInvalid(document, command);
                        break;

                    case CommandKind.InvalidRegister:
                        Report(document, command.Span, DiagnosticCodes.InvalidRegister, command.Text);
                        break;

                    case CommandKind.IgnoredStringOperator:
                        {
                            var suggestion = SuggestStringOperator(command.Text);
                            if (suggestion is null)
                                Report(document, command.Span, DiagnosticCodes.UnknownStringOperator, command.Text, string.Empty);
                            else
                                ReportFix(document, command.Span, DiagnosticCodes.UnknownStringOperator, suggestion, command.Text, $" Did you mean '{suggestion}'?");
                            break;
                        }

                    case CommandKind.IgnoredAccess:
                        Report(document, TextSpan.FromBounds(command.Span.Start, command.NameSpan.Start), DiagnosticCodes.InvalidPrefix, command.Text[..5]);
                        break;
                }
            }
        }

        private void ReportInvalid(ScriptDocument document, Command command)
        {
            var text = command.Text;

            if (text[0] == '\'')
            {
                ReportFix(document, command.Span, DiagnosticCodes.MisplacedComment, "comment");
                return;
            }

            if (text.Contains(',') && CommandClassifier.TryParseNumber(text.Replace(',', '.'), out _))
            {
                ReportFix(document, command.Span, DiagnosticCodes.DecimalComma, text.Replace(',', '.'), text);
                return;
            }

            if (SuggestCaseFix(text) is { } fixedCase)
            {
                ReportFix(document, command.Span, DiagnosticCodes.CaseMismatch, fixedCase, text, fixedCase);
                return;
            }

            Report(document, command.Span, DiagnosticCodes.InvalidCommand, text);
        }

        private static string? SuggestCaseFix(string text)
        {
            foreach (var keyword in KeywordCatalog.Keywords)
            {
                if (string.Equals(keyword.Token, text, StringComparison.OrdinalIgnoreCase))
                    return keyword.Token;
            }

            if (text.StartsWith("{macro:", StringComparison.OrdinalIgnoreCase) && text.EndsWith('}'))
                return "{macro:" + text[7..];
            if (text.StartsWith("{trigger:", StringComparison.OrdinalIgnoreCase) && text.EndsWith('}'))
                return "{trigger:" + text[9..];

            if (OperatorCatalog.FindFloatIgnoreCase(text) is { } op)
                return op.Token;

            if (text.Length == 2 && text[0] is 'S' or 'L' && char.IsAsciiDigit(text[1]))
                return char.ToLowerInvariant(text[0]) + text[1..];

            return null;
        }

        private static string? SuggestStringOperator(string text)
        {
            if (OperatorCatalog.FindStringIgnoreCase(text) is { } op)
                return op.Token;

            return text switch
            {
                "$=>" => "$>=",
                "$=<" => "$<=",
                "$==" => "$=",
                "$SetLengthM" => "$SetLengthC",
                _ => null,
            };
        }

        private void AnalyzeStructure()
        {
            var entries = new Dictionary<CommandKind, StreamItem>();
            StreamItem? openHeader = null;
            var openBlockId = 0;
            var deadStart = -1;
            var blockCounter = 0;

            void FlushDead(int endExclusive)
            {
                if (deadStart < 0)
                    return;

                var first = _stream[deadStart];
                for (var i = deadStart; i < endExclusive; i++)
                {
                    var item = _stream[i];
                    if (i != endExclusive - 1 && _stream[i + 1].Document == item.Document)
                        continue;

                    Report(Doc(item), TextSpan.FromBounds(Cmd(first).Span.Start, Cmd(item).Span.End), DiagnosticCodes.CodeOutsideBlock);
                    if (i + 1 < endExclusive)
                        first = _stream[i + 1];
                }

                deadStart = -1;
            }

            void CloseBlock(StreamItem? endItem)
            {
                if (openHeader is not { } header)
                    return;

                var document = Doc(header);
                var headerCommand = Cmd(header);
                var endCommand = endItem is { } e && e.Document == header.Document ? Cmd(e) : null;
                var endOffset = endCommand?.Span.End ?? LastCommandEnd(header);
                _blocks[document.Path].Add(new BlockInfo(BlockKindOf(headerCommand.Kind), headerCommand.Name, headerCommand, header.Command,
                    endCommand, endCommand is null ? -1 : endItem!.Value.Command, TextSpan.FromBounds(headerCommand.Span.Start, endOffset)));
                openHeader = null;
            }

            for (var i = 0; i < _stream.Count; i++)
            {
                var item = _stream[i];
                var command = Cmd(item);
                var document = Doc(item);

                if (command.IsBlockHeader)
                {
                    FlushDead(i);
                    if (openHeader is { } previous)
                    {
                        ReportFix(Doc(previous), Cmd(previous).Span, DiagnosticCodes.MissingEnd, "end", Cmd(previous).ScopeName);
                        CloseBlock(null);
                    }

                    openHeader = item;
                    openBlockId = ++blockCounter;
                    _blockOf[i] = openBlockId;
                    RegisterHeader(item, command, document, i, entries);
                    continue;
                }

                if (command.Kind == CommandKind.End)
                {
                    FlushDead(i);
                    if (openHeader is null)
                    {
                        Report(document, command.Span, DiagnosticCodes.EndWithoutBlock);
                    }
                    else
                    {
                        _blockOf[i] = openBlockId;
                        CloseBlock(item);
                    }

                    continue;
                }

                if (openHeader is null)
                {
                    if (deadStart < 0)
                        deadStart = i;
                    continue;
                }

                _blockOf[i] = openBlockId;
            }

            FlushDead(_stream.Count);
            if (openHeader is { } last)
            {
                ReportFix(Doc(last), Cmd(last).Span, DiagnosticCodes.MissingEnd, "end", Cmd(last).ScopeName);
                CloseBlock(null);
            }
        }

        private int LastCommandEnd(StreamItem header)
        {
            var document = Documents[header.Document];
            var end = document.Commands[header.Command].Span.End;
            for (var c = header.Command + 1; c < document.Commands.Count; c++)
            {
                if (document.Commands[c].IsBlockHeader)
                    break;
                end = document.Commands[c].Span.End;
            }

            return end;
        }

        private static BlockKind BlockKindOf(CommandKind kind) => kind switch
        {
            CommandKind.Init => BlockKind.Init,
            CommandKind.Frame => BlockKind.Frame,
            CommandKind.FrameAI => BlockKind.FrameAI,
            CommandKind.MacroHeader => BlockKind.Macro,
            _ => BlockKind.Trigger,
        };

        private void RegisterHeader(StreamItem item, Command command, ScriptDocument document, int streamIndex, Dictionary<CommandKind, StreamItem> entries)
        {
            if (command.IsEntryHeader)
            {
                if (entries.TryGetValue(command.Kind, out var first))
                    Report(document, command.Span, DiagnosticCodes.DuplicateEntryPoint, command.ScopeName);
                else
                    entries[command.Kind] = item;
                return;
            }

            var definition = new MacroDefinition(command.Name, document.Path, item.Command, streamIndex, command.NameSpan, command.Span);
            var isMacro = command.Kind == CommandKind.MacroHeader;
            (isMacro ? _macros : _triggers).Add(definition);
            _occurrences.Add(new SymbolOccurrence(isMacro ? SymbolKind.Macro : SymbolKind.Trigger, command.Name, SymbolRole.Definition, document.Path, command.NameSpan, item.Command));

            if (command.Name.Length == 0)
                Report(document, command.Span, DiagnosticCodes.EmptyName, isMacro ? "macro" : "trigger");
        }

        private void AnalyzeConditions()
        {
            var stack = new Stack<(int EndIf, int Else)>();

            for (var i = _stream.Count - 1; i >= 0; i--)
            {
                var command = Cmd(_stream[i]);
                switch (command.Kind)
                {
                    case CommandKind.EndIf:
                        stack.Push((i, -1));
                        break;

                    case CommandKind.Else:
                        if (stack.Count == 0)
                        {
                            Report(Doc(_stream[i]), command.Span, DiagnosticCodes.ElseWithoutEndIf);
                        }
                        else
                        {
                            var top = stack.Pop();
                            if (top.Else >= 0)
                            {
                                var extra = _stream[top.Else];
                                Report(Doc(extra), Cmd(extra).Span, DiagnosticCodes.MultipleElse);
                            }

                            stack.Push((top.EndIf, i));
                        }

                        break;

                    case CommandKind.If:
                        if (stack.Count == 0)
                        {
                            ReportFix(Doc(_stream[i]), command.Span, DiagnosticCodes.IfWithoutEndIf, "endif");
                        }
                        else
                        {
                            var (endIf, elseIndex) = stack.Pop();
                            AddCondition(i, elseIndex, endIf);
                        }

                        break;
                }
            }

            foreach (var (endIf, _) in stack)
                Report(Doc(_stream[endIf]), Cmd(_stream[endIf]).Span, DiagnosticCodes.EndIfWithoutIf);
        }

        private void AddCondition(int ifIndex, int elseIndex, int endIfIndex)
        {
            var ifItem = _stream[ifIndex];
            var endItem = _stream[endIfIndex];
            var ifCommand = Cmd(ifItem);
            var crossesBlock = _blockOf[ifIndex] != _blockOf[endIfIndex] || (elseIndex >= 0 && _blockOf[elseIndex] != _blockOf[ifIndex]);

            if (crossesBlock)
                Report(Doc(ifItem), ifCommand.Span, DiagnosticCodes.ConditionCrossesBlock, Location(Doc(endItem), Cmd(endItem).Span));

            if (ifItem.Document == endItem.Document)
            {
                var elseCommand = elseIndex >= 0 && _stream[elseIndex].Document == ifItem.Document ? Cmd(_stream[elseIndex]) : null;
                _conditions[Doc(ifItem).Path].Add(new ConditionInfo(ifCommand, elseCommand, Cmd(endItem)));
            }
        }

        private void AnalyzeSymbols()
        {
            var lastMacro = LastDefinitions(_macros);
            var lastTrigger = LastDefinitions(_triggers);
            ReportDuplicates(_macros, lastMacro, DiagnosticCodes.DuplicateMacro);
            ReportDuplicates(_triggers, lastTrigger, DiagnosticCodes.DuplicateTrigger);

            var usedMacros = new HashSet<MacroDefinition>();
            for (var i = 0; i < _stream.Count; i++)
            {
                var item = _stream[i];
                var command = Cmd(item);
                var document = Doc(item);
                if (!command.HasName || command.Kind is CommandKind.MacroHeader or CommandKind.TriggerHeader)
                    continue;

                var kind = SymbolKinds.FromCommand(command.Kind)!.Value;
                _occurrences.Add(new SymbolOccurrence(kind, command.Name, SymbolKinds.RoleFromCommand(command.Kind), document.Path, command.NameSpan, item.Command));

                switch (command.Kind)
                {
                    case CommandKind.LoadFloatVariable or CommandKind.StoreFloatVariable:
                        CheckVariable(document, command, context.FloatVariables, DiagnosticCodes.UnknownFloatVariable);
                        break;

                    case CommandKind.LoadStringVariable or CommandKind.StoreStringVariable:
                        CheckVariable(document, command, context.StringVariables, DiagnosticCodes.UnknownStringVariable);
                        break;

                    case CommandKind.LoadSystemVariable or CommandKind.StoreSystemVariable:
                        CheckSystemVariable(document, command);
                        break;

                    case CommandKind.Constant:
                        CheckConstant(document, command);
                        break;

                    case CommandKind.Curve:
                        CheckCurve(document, command);
                        break;

                    case CommandKind.SystemMacroCall:
                        CheckSystemMacro(document, command);
                        break;

                    case CommandKind.MacroCall:
                        CheckMacroCall(document, command, item, i, lastMacro, usedMacros);
                        break;
                }
            }

            if (!context.HasProject)
                return;

            foreach (var macro in lastMacro.Values)
            {
                if (!usedMacros.Contains(macro))
                    Report(DocumentOf(macro.Path), macro.HeaderSpan, DiagnosticCodes.UnusedMacro, macro.Name);
            }
        }

        private static Dictionary<string, MacroDefinition> LastDefinitions(List<MacroDefinition> definitions)
        {
            var last = new Dictionary<string, MacroDefinition>(AsciiIgnoreCaseComparer.Instance);
            foreach (var definition in definitions)
                last[definition.Name] = definition;
            return last;
        }

        private void ReportDuplicates(List<MacroDefinition> definitions, Dictionary<string, MacroDefinition> effective, DiagnosticDescriptor descriptor)
        {
            foreach (var definition in definitions)
            {
                var winner = effective[definition.Name];
                if (!ReferenceEquals(winner, definition))
                    Report(DocumentOf(definition.Path), definition.HeaderSpan, descriptor, definition.Name, Location(DocumentOf(winner.Path), winner.HeaderSpan));
            }
        }

        private void CheckVariable(ScriptDocument document, Command command, SymbolTable<VariableDefinition> table, DiagnosticDescriptor descriptor)
        {
            if (context.HasProject && !table.Contains(command.Name))
                ReportFix(document, command.NameSpan, descriptor, "declare", command.Name, Owner);
        }

        private void CheckSystemVariable(ScriptDocument document, Command command)
        {
            if (command.Text[3] != 'S')
            {
                var canonical = $"({command.Text[1]}.S.{command.Name})";
                ReportFix(document, command.Span, DiagnosticCodes.NonCanonicalPrefix, canonical, canonical, command.Text);
            }

            if (command.Kind == CommandKind.StoreSystemVariable)
                Report(document, command.Span, DiagnosticCodes.SystemVariableWrite, command.Name);

            if (!context.SystemVariables.Contains(command.Name))
                Report(document, command.NameSpan, DiagnosticCodes.UnknownSystemVariable, command.Name);
        }

        private void CheckConstant(ScriptDocument document, Command command)
        {
            if (command.Text[3] != 'L')
            {
                var canonical = $"(C.L.{command.Name})";
                ReportFix(document, command.Span, DiagnosticCodes.NonCanonicalPrefix, canonical, canonical, command.Text);
            }

            if (context.HasProject && !context.Constants.Contains(command.Name))
                Report(document, command.NameSpan, DiagnosticCodes.UnknownConstant, command.Name, Owner);
        }

        private void CheckCurve(ScriptDocument document, Command command)
        {
            if (command.Text[3] != 'L')
            {
                ReportFix(document, command.Span, DiagnosticCodes.CurvePrefix, $"(F.L.{command.Name})", command.Name, command.Text);
                return;
            }

            if (context.HasProject && !context.Curves.Contains(command.Name))
                Report(document, command.NameSpan, DiagnosticCodes.UnknownCurve, command.Name, Owner);
        }

        private void CheckSystemMacro(ScriptDocument document, Command command)
        {
            if (context.Macros.Contains(command.Name))
                return;

            if (SystemMacroCatalog.Find(command.Name) is not null && context.Kind != ObjectKind.Unknown)
                Report(document, command.NameSpan, DiagnosticCodes.SystemMacroUnavailable, command.Name, ObjectKinds.DisplayName(context.Kind));
            else
                Report(document, command.NameSpan, DiagnosticCodes.UnknownSystemMacro, command.Name);
        }

        private void CheckMacroCall(ScriptDocument document, Command command, StreamItem item, int streamIndex,
            Dictionary<string, MacroDefinition> lastDefinition, HashSet<MacroDefinition> used)
        {
            if (lastDefinition.TryGetValue(command.Name, out var definition))
            {
                _resolutions[ContextAnalysis.Key(document.Path, item.Command)] = definition;
                if (definition.StreamIndex > streamIndex)
                {
                    used.Add(definition);
                    return;
                }

                var severity = context.HasProject ? Severity.Error : Severity.Warning;
                if (_blockOf[streamIndex] == _blockOf[definition.StreamIndex])
                    ReportWithSeverity(document, command.NameSpan, DiagnosticCodes.RecursiveMacro, severity, command.Name);
                else
                    ReportWithSeverity(document, command.NameSpan, DiagnosticCodes.MacroDefinedBeforeCall, severity, command.Name,
                        Location(DocumentOf(definition.Path), definition.HeaderSpan));
                return;
            }

            if (!context.HasProject)
                Report(document, command.NameSpan, DiagnosticCodes.MacroNotInFile, command.Name);
            else
                ReportFix(document, command.NameSpan, DiagnosticCodes.UnknownMacro, "macro", command.Name, Owner);
        }

        private void AnalyzeRegisters()
        {
            foreach (var document in Documents)
            {
                foreach (var block in _blocks[document.Path])
                {
                    if (block.Kind == BlockKind.Macro)
                        continue;

                    var written = new bool[10];
                    var end = block.EndIndex < 0 ? LastIndexInBlock(document, block.HeaderIndex) : block.EndIndex;
                    for (var c = block.HeaderIndex + 1; c < end; c++)
                    {
                        var command = document.Commands[c];
                        switch (command.Kind)
                        {
                            case CommandKind.StoreRegister:
                                written[command.Register] = true;
                                break;
                            case CommandKind.LoadRegister when !written[command.Register]:
                                Report(document, command.Span, DiagnosticCodes.RegisterNotWritten, command.Text);
                                written[command.Register] = true;
                                break;
                            case CommandKind.MacroCall:
                                Array.Fill(written, true);
                                break;
                        }
                    }
                }
            }
        }

        private static int LastIndexInBlock(ScriptDocument document, int header)
        {
            var c = header + 1;
            while (c < document.Commands.Count && !document.Commands[c].IsBlockHeader && document.Commands[c].Kind != CommandKind.End)
                c++;
            return c;
        }

        private void AnalyzeOperators()
        {
            foreach (var document in Documents)
            {
                var commands = document.Commands;
                for (var c = 0; c < commands.Count; c++)
                {
                    var command = commands[c];
                    switch (command.Operator?.Id)
                    {
                        case "str.msg" or "stackdump":
                            Report(document, command.Span, DiagnosticCodes.DebugCommand, command.Text);
                            break;

                        case "str.intToStrEnh" when Operand(commands, c, CommandKind.StringLiteral) is { } format && !IsValidIntToStrEnhFormat(format.Name):
                            Report(document, format.Span, DiagnosticCodes.IntToStrEnhFormat, format.Name);
                            break;

                        case "str.repeat" when Operand(commands, c, CommandKind.Number) is { } length && Math.Round(length.Value) >= 1000:
                            Report(document, command.Span, DiagnosticCodes.RepeatTooLong);
                            break;
                    }
                }
            }
        }

        private static Command? Operand(IReadOnlyList<Command> commands, int index, CommandKind kind)
        {
            static bool PushesFloat(Command c) => c.Kind is CommandKind.Number or CommandKind.LoadFloatVariable or CommandKind.LoadSystemVariable
                or CommandKind.Constant or CommandKind.LoadRegister;

            static bool PushesString(Command c) => c.Kind is CommandKind.StringLiteral or CommandKind.LoadStringVariable;

            if (index >= 1 && commands[index - 1].Kind == kind)
                return commands[index - 1];

            if (index >= 2 && commands[index - 2].Kind == kind)
            {
                var between = commands[index - 1];
                if (kind == CommandKind.StringLiteral ? PushesFloat(between) : PushesString(between))
                    return commands[index - 2];
            }

            return null;
        }

        private static bool IsValidIntToStrEnhFormat(string format)
            => format.Length >= 2
               && int.TryParse(format.AsSpan(1), NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var length)
               && length >= 0;
    }
}
