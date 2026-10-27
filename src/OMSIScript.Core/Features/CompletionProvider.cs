using System.Globalization;
using OMSIScript.Analysis;
using OMSIScript.Documentation;
using OMSIScript.Language;
using OMSIScript.Project;
using OMSIScript.Text;

namespace OMSIScript.Features;

public enum CompletionKind
{
    Keyword,
    Snippet,
    Operator,
    Variable,
    Function,
    Constant,
    Event,
    Module,
    Value,
}

public sealed record CompletionEntry(
    string Label,
    CompletionKind Kind,
    string InsertText,
    TextSpan ReplaceSpan,
    string? Detail = null,
    string? Documentation = null,
    bool IsSnippet = false,
    string? FilterText = null,
    string? SortText = null,
    bool Retrigger = false);

public static class CompletionProvider
{
    private static readonly (string Prefix, string Label, string DocKey)[] AccessPrefixes =
    [
        ("(L.L.", "load variable", "access.LL"),
        ("(S.L.", "store variable", "access.SL"),
        ("(L.$.", "load string variable", "access.L$"),
        ("(S.$.", "store string variable", "access.S$"),
        ("(L.S.", "load system variable", "access.LS"),
        ("(M.L.", "call macro", "access.ML"),
        ("(M.V.", "call system macro", "access.MV"),
        ("(C.L.", "load constant", "access.CL"),
        ("(F.L.", "evaluate curve", "access.FL"),
        ("(T.L.", "fire sound trigger", "access.TL"),
        ("(T.F.", "fire sound trigger with file", "access.TF"),
    ];

    public static IReadOnlyList<CompletionEntry> GetCompletions(FeatureContext context, int offset)
    {
        var text = context.Document.Source.Text;
        var source = context.Document.Source;
        var line = source.GetLineSpan(source.GetLineIndex(offset));
        if (line.Length > 0 && text[line.Start] == '\'')
            return [];

        var start = offset;
        while (start > line.Start && text[start - 1] is not (' ' or '\t'))
            start--;

        var quotes = 0;
        for (var i = line.Start; i < start; i++)
        {
            if (text[i] == '"')
                quotes++;
        }

        var prefix = text[start..offset];
        if (quotes % 2 == 1 || prefix.StartsWith('"'))
            return [];

        var next = offset < text.Length ? text[offset] : '\0';

        if (prefix.Length >= 5 && prefix[0] == '(' && prefix[2] == '.' && prefix[4] == '.')
            return Names(context, prefix[1..4], TextSpan.FromBounds(start + 5, next == ')' ? offset + 1 : offset), prefix[5..]);

        if (prefix.StartsWith('('))
            return Prefixes(TextSpan.FromBounds(start, next == ')' ? offset + 1 : offset));

        if (prefix.StartsWith("{trigger:", StringComparison.Ordinal))
            return TriggerNames(context, TextSpan.FromBounds(start + 9, next == '}' ? offset + 1 : offset));

        var replace = TextSpan.FromBounds(start, prefix.StartsWith('{') && next == '}' ? offset + 1 : offset);
        if (prefix.StartsWith('{'))
            return Keywords(replace, withBrace: true);

        if (prefix.StartsWith('$'))
            return [.. StringOperators(replace)];

        return [.. Keywords(replace, withBrace: false), .. FloatOperators(replace), .. StringOperators(replace), .. Prefixes(replace), .. Registers(replace)];
    }

    private static List<CompletionEntry> Keywords(TextSpan replace, bool withBrace)
    {
        string Filter(string token) => withBrace ? token : token.TrimStart('{');

        var items = new List<CompletionEntry>();
        foreach (var keyword in KeywordCatalog.Keywords)
        {
            items.Add(new CompletionEntry(keyword.Token, CompletionKind.Keyword, keyword.Token, replace,
                Documentation: Docs.Get(keyword.DocumentationKey), FilterText: Filter(keyword.Token), SortText: "0" + keyword.Token));
        }

        items.Add(Snippet("{macro:name} … {end}", "{macro:${1:name}}\n\t$0\n{end}", KeywordCatalog.Macro.DocumentationKey, replace, Filter("{macro:")));
        items.Add(Snippet("{trigger:name} … {end}", "{trigger:${1:name}}\n\t$0\n{end}", KeywordCatalog.Trigger.DocumentationKey, replace, Filter("{trigger:")));
        items.Add(Snippet("{init} … {end}", "{init}\n\t$0\n{end}", "kw.init", replace, Filter("{init}")));
        items.Add(Snippet("{frame} … {end}", "{frame}\n\t$0\n{end}", "kw.frame", replace, Filter("{frame}")));
        items.Add(Snippet("{if} … {endif}", "{if}\n\t$0\n{endif}", "kw.if", replace, Filter("{if}")));
        items.Add(Snippet("{if} … {else} … {endif}", "{if}\n\t$1\n{else}\n\t$0\n{endif}", "kw.else", replace, Filter("{if}")));
        return items;
    }

    private static CompletionEntry Snippet(string label, string body, string docKey, TextSpan replace, string filter)
        => new(label, CompletionKind.Snippet, body, replace, Documentation: Docs.Get(docKey), IsSnippet: true, FilterText: filter, SortText: "1" + label);

    private static IEnumerable<CompletionEntry> FloatOperators(TextSpan replace)
        => OperatorCatalog.FloatOperators.Select(op => new CompletionEntry(op.Token, CompletionKind.Operator, op.Token, replace,
            Docs.StackEffect(op.Effect), Docs.Get(op.DocumentationKey), SortText: "2" + op.Token));

    private static IEnumerable<CompletionEntry> StringOperators(TextSpan replace)
        => OperatorCatalog.StringOperators.Select(op => new CompletionEntry(op.Token, CompletionKind.Operator, op.Token, replace,
            Docs.StackEffect(op.Effect), Docs.Get(op.DocumentationKey), SortText: "3" + op.Token));

    private static IEnumerable<CompletionEntry> Registers(TextSpan replace)
    {
        for (var i = 0; i < 10; i++)
        {
            yield return new CompletionEntry($"l{i}", CompletionKind.Variable, $"l{i}", replace, "load register", Docs.Format("reg.load", i), SortText: $"5l{i}");
            yield return new CompletionEntry($"s{i}", CompletionKind.Variable, $"s{i}", replace, "store register", Docs.Format("reg.store", i), SortText: $"5s{i}");
        }
    }

    private static List<CompletionEntry> Prefixes(TextSpan replace)
        => [.. AccessPrefixes.Select(p => new CompletionEntry(p.Prefix + "…)", CompletionKind.Module, p.Prefix, replace, p.Label, Docs.Get(p.DocKey),
            FilterText: p.Prefix, SortText: "4" + p.Prefix, Retrigger: true))];

    private static IReadOnlyList<CompletionEntry> Names(FeatureContext context, string kind, TextSpan replace, string typed)
    {
        var entries = new Dictionary<string, CompletionEntry>(AsciiIgnoreCaseComparer.Instance);
        var analyses = context.Analyses;

        void Add(string name, CompletionKind completionKind, string? detail, string? documentation, int rank = 1)
        {
            if (name.Length == 0 || name.Contains(' ') || name.Contains('\t') || entries.ContainsKey(name))
                return;
            entries[name] = new CompletionEntry(name, completionKind, name + ")", replace, detail, documentation, FilterText: name,
                SortText: rank.ToString(CultureInfo.InvariantCulture) + name);
        }

        switch (kind)
        {
            case "L.L" or "S.L":
                foreach (var analysis in analyses)
                {
                    foreach (var variable in analysis.Context.FloatVariables.Items)
                        Add(variable.Name, CompletionKind.Variable, VariableDetail(variable), Docs.Variable(analysis.Context.Kind, variable.Name, isString: false), variable.Origin == VariableOrigin.VarList ? 0 : 1);
                }

                AddUsed(SymbolKind.FloatVariable);
                break;

            case "L.$" or "S.$":
                foreach (var analysis in analyses)
                {
                    foreach (var variable in analysis.Context.StringVariables.Items)
                        Add(variable.Name, CompletionKind.Variable, VariableDetail(variable), Docs.Variable(analysis.Context.Kind, variable.Name, isString: true), variable.Origin == VariableOrigin.VarList ? 0 : 1);
                }

                AddUsed(SymbolKind.StringVariable);
                break;

            case "L.S" or "S.S":
                foreach (var name in analyses.FirstOrDefault()?.Context.SystemVariables.Names ?? GameData.Default.SystemVariableList.Names)
                    Add(name, CompletionKind.Variable, "system variable", Docs.Get("sysvar." + name));
                break;

            case "M.L":
                foreach (var analysis in analyses)
                {
                    foreach (var macro in analysis.Macros)
                    {
                        var source = context.GetSource(macro.Path);
                        var line = source?.GetPosition(macro.HeaderSpan.Start).Line + 1;
                        Add(macro.Name, CompletionKind.Function, $"{Path.GetFileName(macro.Path)}:{line}", Docs.Get(KeywordCatalog.Macro.DocumentationKey));
                    }
                }

                break;

            case "M.V":
                foreach (var name in (analyses.FirstOrDefault()?.Context.Macros.Names ?? GameData.Default.Unknown.Macros.Names).Distinct(AsciiIgnoreCaseComparer.Instance))
                {
                    var info = SystemMacroCatalog.Find(name);
                    Add(name, CompletionKind.Function, info is null ? "system macro" : Docs.StackEffect(info.Effect), info is null ? null : Docs.Get(info.DocumentationKey));
                }

                break;

            case "C.L":
                foreach (var analysis in analyses)
                {
                    foreach (var constant in analysis.Context.Constants.Items)
                        Add(constant.Name, CompletionKind.Constant, $"= {constant.Value.ToString("R", CultureInfo.InvariantCulture)}", $"`{Path.GetFileName(constant.Path)}` line {constant.Line + 1}");
                }

                AddUsed(SymbolKind.Constant);
                break;

            case "F.L":
                foreach (var analysis in analyses)
                {
                    foreach (var curve in analysis.Context.Curves.Items)
                        Add(curve.Name, CompletionKind.Function, $"curve, {curve.Points.Count} points", $"`{Path.GetFileName(curve.Path)}` line {curve.Line + 1}");
                }

                AddUsed(SymbolKind.Curve);
                break;

            case "T.L" or "T.F":
                AddUsed(SymbolKind.SoundTrigger);
                break;
        }

        return [.. entries.Values];

        void AddUsed(SymbolKind symbolKind)
        {
            foreach (var occurrence in analyses.SelectMany(a => a.Occurrences).Where(o => o.Kind == symbolKind))
            {
                if (!string.Equals(occurrence.Name, typed, StringComparison.Ordinal))
                    Add(occurrence.Name, symbolKind == SymbolKind.SoundTrigger ? CompletionKind.Event : CompletionKind.Variable, "used in scripts", null, 2);
            }
        }
    }

    private static string VariableDetail(VariableDefinition variable)
        => variable.Origin == VariableOrigin.Predefined
            ? "predefined"
            : $"{Path.GetFileName(variable.Path)}:{variable.Line + 1}";

    private static IReadOnlyList<CompletionEntry> TriggerNames(FeatureContext context, TextSpan replace)
    {
        var entries = new Dictionary<string, CompletionEntry>(AsciiIgnoreCaseComparer.Instance);
        foreach (var name in KeyboardTriggers.EngineTriggers)
        {
            var indexed = name.Contains('#');
            var insert = indexed ? name.Replace("#", "${1:0}", StringComparison.Ordinal) + "}" : name + "}";
            entries.TryAdd(name, new CompletionEntry(name, CompletionKind.Event, insert, replace, "system trigger", Docs.Get("trigger." + name),
                IsSnippet: indexed, FilterText: name.Replace("#", string.Empty, StringComparison.Ordinal), SortText: "0" + name));
        }

        foreach (var name in KeyboardTriggers.GetVehicleTriggers(context.Workspace, context.Path))
        {
            entries.TryAdd(name, new CompletionEntry(name, CompletionKind.Event, name + "}", replace, "keyboard trigger", Docs.Format("trigger.keyboard", name), FilterText: name, SortText: "1" + name));
            entries.TryAdd(name + "_off", new CompletionEntry(name + "_off", CompletionKind.Event, name + "_off}", replace, "keyboard trigger (released)", Docs.Format("trigger.keyboard", name), FilterText: name + "_off", SortText: "2" + name));
        }

        return [.. entries.Values];
    }
}
