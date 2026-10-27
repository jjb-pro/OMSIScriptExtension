using OMSIScript.Analysis;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record CodeActionEntry(string Title, IReadOnlyList<TextChange> Changes, bool IsPreferred, ScriptDiagnostic Diagnostic);

public static class CodeActionProvider
{
    public static IReadOnlyList<CodeActionEntry> GetActions(FeatureContext context, IEnumerable<ScriptDiagnostic> diagnostics)
    {
        var actions = new List<CodeActionEntry>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Fix is not { } fix)
                continue;

            var code = diagnostic.Descriptor;
            if (code == DiagnosticCodes.MisplacedComment)
                AddCommentFix(context, diagnostic, actions);
            else if (code == DiagnosticCodes.MissingEnd)
                AddMissingEnd(context, diagnostic, actions);
            else if (code == DiagnosticCodes.IfWithoutEndIf)
                AddMissingEndIf(context, diagnostic, actions);
            else if (code == DiagnosticCodes.UnknownFloatVariable || code == DiagnosticCodes.UnknownStringVariable)
                AddDeclarations(context, diagnostic, actions);
            else if (code == DiagnosticCodes.UnknownMacro)
                AddMacroStub(context, diagnostic, actions);
            else
                actions.Add(new CodeActionEntry($"Replace with '{fix}'", [new TextChange(diagnostic.Path, diagnostic.Span, fix)], true, diagnostic));
        }

        return actions;
    }

    private static string LineBreak(SourceText source) => source.Text.Contains("\r\n", StringComparison.Ordinal) || !source.Text.Contains('\n') ? "\r\n" : "\n";

    private static void AddCommentFix(FeatureContext context, ScriptDiagnostic diagnostic, List<CodeActionEntry> actions)
    {
        var source = context.Document.Source;
        var line = source.GetLineSpan(source.GetLineIndex(diagnostic.Span.Start));
        var before = source.Text.AsSpan(line.Start, diagnostic.Span.Start - line.Start);
        TextChange change;
        if (before.Trim(" \t").IsEmpty)
        {
            change = new TextChange(diagnostic.Path, TextSpan.FromBounds(line.Start, diagnostic.Span.Start), string.Empty);
        }
        else
        {
            var whitespaceStart = diagnostic.Span.Start;
            while (whitespaceStart > line.Start && source.Text[whitespaceStart - 1] is ' ' or '\t')
                whitespaceStart--;
            change = new TextChange(diagnostic.Path, TextSpan.FromBounds(whitespaceStart, diagnostic.Span.Start), LineBreak(source));
        }

        actions.Add(new CodeActionEntry("Move the comment to the start of a line", [change], true, diagnostic));
    }

    private static void AddMissingEnd(FeatureContext context, ScriptDiagnostic diagnostic, List<CodeActionEntry> actions)
    {
        if (context.Structure?.Blocks.FirstOrDefault(b => b.Header.Span == diagnostic.Span) is not { } block)
            return;

        var source = context.Document.Source;
        var line = source.GetLineSpan(source.GetLineIndex(block.Span.End));
        actions.Add(new CodeActionEntry("Add '{end}'", [new TextChange(diagnostic.Path, new TextSpan(line.End, 0), LineBreak(source) + "{end}")], true, diagnostic));
    }

    private static void AddMissingEndIf(FeatureContext context, ScriptDiagnostic diagnostic, List<CodeActionEntry> actions)
    {
        if (context.Structure?.FindBlock(diagnostic.Span.Start) is not { } block)
            return;

        var source = context.Document.Source;
        TextChange change;
        if (block.End is { } end)
        {
            var endLine = source.GetLineSpan(source.GetLineIndex(end.Span.Start));
            change = new TextChange(diagnostic.Path, new TextSpan(endLine.Start, 0), "\t{endif}" + LineBreak(source));
        }
        else
        {
            var line = source.GetLineSpan(source.GetLineIndex(block.Span.End));
            change = new TextChange(diagnostic.Path, new TextSpan(line.End, 0), LineBreak(source) + "\t{endif}");
        }

        actions.Add(new CodeActionEntry("Add '{endif}' at the end of the block", [change], true, diagnostic));
    }

    private static void AddDeclarations(FeatureContext context, ScriptDiagnostic diagnostic, List<CodeActionEntry> actions)
    {
        var name = context.Document.Source.Text.Substring(diagnostic.Span.Start, diagnostic.Span.Length);
        var isString = diagnostic.Descriptor == DiagnosticCodes.UnknownStringVariable;
        var scriptName = Path.GetFileNameWithoutExtension(context.Path);

        var lists = context.Analyses
            .SelectMany(a => isString ? a.Context.StringVarLists : a.Context.VarLists)
            .Where(l => l.Exists)
            .DistinctBy(l => PathComparer.Normalize(l.Path))
            .OrderByDescending(l => Similarity(Path.GetFileNameWithoutExtension(l.Path), scriptName))
            .ToList();

        for (var i = 0; i < lists.Count; i++)
        {
            var list = lists[i];
            var text = context.Workspace.ReadText(list.Path) ?? string.Empty;
            var lineBreak = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
            var insert = text.Length == 0 || text.EndsWith('\n') || text.EndsWith('\r') ? name + lineBreak : lineBreak + name;
            actions.Add(new CodeActionEntry($"Declare '{name}' in {Path.GetFileName(list.Path)}",
                [new TextChange(list.Path, new TextSpan(text.Length, 0), insert)], i == 0, diagnostic));
        }
    }

    private static int Similarity(string a, string b)
    {
        var length = 0;
        while (length < a.Length && length < b.Length && char.ToLowerInvariant(a[length]) == char.ToLowerInvariant(b[length]))
            length++;
        return length;
    }

    private static void AddMacroStub(FeatureContext context, ScriptDiagnostic diagnostic, List<CodeActionEntry> actions)
    {
        var source = context.Document.Source;
        var name = source.Text.Substring(diagnostic.Span.Start, diagnostic.Span.Length);
        var lineBreak = LineBreak(source);
        var prefix = source.Text.Length == 0 || source.Text.EndsWith('\n') ? lineBreak : lineBreak + lineBreak;
        var stub = $"{prefix}{{macro:{name}}}{lineBreak}\t{lineBreak}{{end}}{lineBreak}";
        actions.Add(new CodeActionEntry($"Generate macro '{name}' at the end of this file",
            [new TextChange(context.Path, new TextSpan(source.Length, 0), stub)], false, diagnostic));
    }
}
