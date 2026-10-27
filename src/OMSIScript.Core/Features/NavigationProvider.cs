using OMSIScript.Analysis;
using OMSIScript.Language;
using OMSIScript.Project;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record SymbolTarget(SymbolKind Kind, string Name, TextSpan Span, Command Command, int CommandIndex);

public sealed record TextChange(string Path, TextSpan Span, string NewText);

public sealed record HighlightEntry(TextSpan Span, bool IsWrite);

public static class NavigationProvider
{
    public static SymbolTarget? SymbolAt(FeatureContext context, int offset)
    {
        if (context.CommandAt(offset) is not var (command, index))
            return null;

        if (SymbolKinds.FromCommand(command.Kind) is not { } kind)
            return null;

        if (kind == SymbolKind.Register)
            return new SymbolTarget(kind, command.Register.ToString(System.Globalization.CultureInfo.InvariantCulture), command.Span, command, index);

        return new SymbolTarget(kind, command.Name, command.NameSpan, command, index);
    }

    public static IReadOnlyList<Location> GetDefinitions(FeatureContext context, int offset)
    {
        if (SymbolAt(context, offset) is not { } symbol)
            return [];

        var locations = new List<Location>();
        switch (symbol.Kind)
        {
            case SymbolKind.Macro when symbol.Command.Kind == CommandKind.MacroCall:
                foreach (var analysis in context.Analyses)
                {
                    if (analysis.ResolveMacroCall(context.Path, symbol.CommandIndex) is { } resolved)
                        locations.Add(new Location(resolved.Path, resolved.NameSpan));
                    else
                        locations.AddRange(analysis.FindMacroDefinitions(symbol.Name).Select(m => new Location(m.Path, m.NameSpan)));
                }

                break;

            case SymbolKind.Macro or SymbolKind.Trigger:
                locations.Add(new Location(context.Path, symbol.Span));
                break;

            case SymbolKind.FloatVariable or SymbolKind.StringVariable:
                foreach (var analysis in context.Analyses)
                {
                    var table = symbol.Kind == SymbolKind.FloatVariable ? analysis.Context.FloatVariables : analysis.Context.StringVariables;
                    if (table.Find(symbol.Name) is { } variable && context.LineLocation(variable.Path, variable.Line) is { } location)
                        locations.Add(location);
                }

                break;

            case SymbolKind.Constant:
                foreach (var analysis in context.Analyses)
                {
                    if (analysis.Context.Constants.Find(symbol.Name) is { } constant && context.LineLocation(constant.Path, constant.Line) is { } location)
                        locations.Add(location);
                }

                break;

            case SymbolKind.Curve:
                foreach (var analysis in context.Analyses)
                {
                    if (analysis.Context.Curves.Find(symbol.Name) is { } curve && context.LineLocation(curve.Path, curve.Line) is { } location)
                        locations.Add(location);
                }

                break;

            case SymbolKind.Register:
                if (context.Structure?.FindBlock(symbol.Command.Span.Start) is { } block)
                {
                    var store = context.Document.Commands
                        .Take(symbol.CommandIndex)
                        .Select((c, i) => (Command: c, Index: i))
                        .LastOrDefault(c => c.Index > block.HeaderIndex && c.Command.Kind == CommandKind.StoreRegister && c.Command.Register == symbol.Command.Register);
                    if (store.Command is not null)
                        locations.Add(new Location(context.Path, store.Command.Span));
                }

                break;
        }

        return Distinct(locations);
    }

    public static IReadOnlyList<Location> GetReferences(FeatureContext context, int offset, bool includeDeclaration)
    {
        if (SymbolAt(context, offset) is not { } symbol)
            return [];

        if (symbol.Kind == SymbolKind.Register)
            return [.. RegisterOccurrences(context, symbol).Select(c => new Location(context.Path, c.Span))];

        var locations = new List<Location>();
        foreach (var occurrence in Occurrences(context.Analyses, symbol))
        {
            if (includeDeclaration || occurrence.Role != SymbolRole.Definition)
                locations.Add(new Location(occurrence.Path, occurrence.Span));
        }

        if (includeDeclaration)
            locations.AddRange(Declarations(context, context.Analyses, symbol).Select(d => new Location(d.Path, d.Span)));

        return Distinct(locations);
    }

    public static IReadOnlyList<HighlightEntry> GetHighlights(FeatureContext context, int offset)
    {
        if (SymbolAt(context, offset) is not { } symbol)
            return [];

        if (symbol.Kind == SymbolKind.Register)
            return [.. RegisterOccurrences(context, symbol).Select(c => new HighlightEntry(c.Span, c.Kind == CommandKind.StoreRegister))];

        return [.. Occurrences(context.Analyses, symbol)
            .Where(o => PathComparer.Instance.Equals(o.Path, context.Path))
            .DistinctBy(o => o.Span)
            .Select(o => new HighlightEntry(o.Span, o.Role is SymbolRole.Write or SymbolRole.Definition))];
    }

    public static (TextSpan Span, string Name)? PrepareRename(FeatureContext context, int offset, out string? error)
    {
        error = null;
        if (SymbolAt(context, offset) is not { } symbol)
        {
            error = "Nothing to rename here.";
            return null;
        }

        switch (symbol.Kind)
        {
            case SymbolKind.Macro or SymbolKind.Trigger or SymbolKind.Constant or SymbolKind.Curve:
                return (symbol.Span, symbol.Name);

            case SymbolKind.FloatVariable or SymbolKind.StringVariable:
                foreach (var analysis in context.Analyses)
                {
                    var table = symbol.Kind == SymbolKind.FloatVariable ? analysis.Context.FloatVariables : analysis.Context.StringVariables;
                    if (table.Find(symbol.Name) is { Origin: VariableOrigin.Predefined })
                    {
                        error = $"'{symbol.Name}' is predefined by OMSI and cannot be renamed.";
                        return null;
                    }
                }

                return (symbol.Span, symbol.Name);

            default:
                error = $"A {SymbolKinds.DisplayName(symbol.Kind)} cannot be renamed.";
                return null;
        }
    }

    public static IReadOnlyList<TextChange>? Rename(FeatureContext context, int offset, string newName, out string? error)
    {
        if (PrepareRename(context, offset, out error) is null)
            return null;

        var symbol = SymbolAt(context, offset)!;
        if (newName.Length == 0 || newName.IndexOfAny([' ', '\t', '"', '\r', '\n']) >= 0
            || (symbol.Kind is SymbolKind.Macro or SymbolKind.Trigger && newName.Contains('}'))
            || (symbol.Kind is not (SymbolKind.Macro or SymbolKind.Trigger) && newName.Contains(')')))
        {
            error = $"'{newName}' is not a valid {SymbolKinds.DisplayName(symbol.Kind)} name.";
            return null;
        }

        var analyses = AffectedAnalyses(context, symbol);
        var changes = new List<TextChange>();
        foreach (var occurrence in Occurrences(analyses, symbol))
            changes.Add(new TextChange(occurrence.Path, occurrence.Span, newName));
        foreach (var declaration in Declarations(context, analyses, symbol))
            changes.Add(new TextChange(declaration.Path, declaration.Span, newName));

        return [.. changes.DistinctBy(c => (PathComparer.Normalize(c.Path), c.Span))];
    }

    private static IReadOnlyList<ContextAnalysis> AffectedAnalyses(FeatureContext context, SymbolTarget symbol)
    {
        var analyses = context.Analyses.ToList();
        var owners = new HashSet<string>(analyses.Where(a => a.Context.Owner is not null).Select(a => a.Context.Owner!.Path), PathComparer.Instance);
        foreach (var declaration in Declarations(context, context.Analyses, symbol))
        {
            foreach (var dependent in context.Workspace.GetDependents(declaration.Path))
            {
                if (owners.Add(dependent.Path))
                    analyses.Add(Analyzer.Analyze(context.Workspace.CreateContext(dependent)));
            }
        }

        return analyses;
    }

    private static IEnumerable<SymbolOccurrence> Occurrences(IEnumerable<ContextAnalysis> analyses, SymbolTarget symbol)
        => analyses.SelectMany(a => a.Occurrences)
            .Where(o => o.Kind == symbol.Kind && AsciiIgnoreCaseComparer.Instance.Equals(o.Name, symbol.Name))
            .DistinctBy(o => (PathComparer.Normalize(o.Path), o.Span));

    private static IEnumerable<Location> Declarations(FeatureContext context, IEnumerable<ContextAnalysis> analyses, SymbolTarget symbol)
    {
        foreach (var analysis in analyses)
        {
            (string Path, int Line)? declaration = symbol.Kind switch
            {
                SymbolKind.FloatVariable => analysis.Context.FloatVariables.Find(symbol.Name) is { Origin: VariableOrigin.VarList } v ? (v.Path, v.Line) : null,
                SymbolKind.StringVariable => analysis.Context.StringVariables.Find(symbol.Name) is { Origin: VariableOrigin.VarList } v ? (v.Path, v.Line) : null,
                SymbolKind.Constant => analysis.Context.Constants.Find(symbol.Name) is { } c ? (c.Path, c.Line) : null,
                SymbolKind.Curve => analysis.Context.Curves.Find(symbol.Name) is { } c ? (c.Path, c.Line) : null,
                _ => null,
            };

            if (declaration is { } d && context.LineLocation(d.Path, d.Line) is { } location)
                yield return location;
        }
    }

    private static IEnumerable<Command> RegisterOccurrences(FeatureContext context, SymbolTarget symbol)
    {
        if (context.Structure?.FindBlock(symbol.Command.Span.Start) is not { } block)
            return [symbol.Command];

        var end = block.EndIndex < 0 ? context.Document.Commands.Count : block.EndIndex;
        return context.Document.Commands
            .Skip(block.HeaderIndex + 1)
            .Take(Math.Max(0, end - block.HeaderIndex - 1))
            .Where(c => c.Kind is CommandKind.LoadRegister or CommandKind.StoreRegister && c.Register == symbol.Command.Register);
    }

    private static IReadOnlyList<Location> Distinct(List<Location> locations)
        => [.. locations.DistinctBy(l => (PathComparer.Normalize(l.Path), l.Span))];
}
