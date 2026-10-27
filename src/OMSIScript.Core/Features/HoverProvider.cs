using System.Globalization;
using System.Text;
using OMSIScript.Analysis;
using OMSIScript.Documentation;
using OMSIScript.Language;
using OMSIScript.Project;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record HoverResult(string Markdown, TextSpan Span);

public static class HoverProvider
{
    public static HoverResult? GetHover(FeatureContext context, int offset)
    {
        if (context.CommandAt(offset) is not var (command, index))
            return null;

        var builder = new StringBuilder();
        Describe(context, command, index, builder);
        return builder.Length == 0 ? null : new HoverResult(builder.ToString().TrimEnd(), command.Span);
    }

    private static void Describe(FeatureContext context, Command command, int index, StringBuilder builder)
    {
        void Header(string code) => builder.Append("```osc\n").Append(code).Append("\n```\n");

        void Paragraph(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (builder.Length > 0 && !builder.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                builder.Append('\n');
            builder.Append(text).Append("\n\n");
        }

        void Effect(StackEffect effect) => Paragraph($"*{Docs.StackEffect(effect)}*");

        switch (command.Kind)
        {
            case CommandKind.StringLiteral:
                Header(command.Text);
                Paragraph(Docs.Get("literal.string"));
                break;

            case CommandKind.Number:
                Header(command.Text);
                Paragraph(Docs.Get("literal.number"));
                break;

            case CommandKind.Init or CommandKind.Frame or CommandKind.FrameAI or CommandKind.End or CommandKind.If or CommandKind.Else or CommandKind.EndIf:
                Header(command.Text);
                Paragraph(Docs.Get(KeywordCatalog.Find(command.Text)!.DocumentationKey));
                break;

            case CommandKind.MacroHeader:
                Header(command.Text);
                Paragraph(Docs.Get(KeywordCatalog.Macro.DocumentationKey));
                Paragraph(UsageSummary(context, SymbolKind.Macro, command.Name));
                break;

            case CommandKind.TriggerHeader:
                Header(command.Text);
                Paragraph(Docs.Get(KeywordCatalog.Trigger.DocumentationKey));
                Paragraph(TriggerSource(context, command.Name));
                break;

            case CommandKind.LoadRegister:
                Header(command.Text);
                Paragraph(Docs.Format("reg.load", command.Register));
                Effect(new StackEffect(0, 1, 0, 0));
                break;

            case CommandKind.StoreRegister:
                Header(command.Text);
                Paragraph(Docs.Format("reg.store", command.Register));
                break;

            case CommandKind.Operator or CommandKind.StringOperator:
                Header(command.Text);
                Paragraph(Docs.Get(command.Operator!.DocumentationKey));
                Effect(command.Operator.Effect);
                break;

            case CommandKind.LoadFloatVariable or CommandKind.StoreFloatVariable:
                Header(command.Text);
                Paragraph(DescribeVariable(context, command.Name, isString: false));
                Paragraph(Docs.Get(command.Kind == CommandKind.LoadFloatVariable ? "access.LL" : "access.SL"));
                break;

            case CommandKind.LoadStringVariable or CommandKind.StoreStringVariable:
                Header(command.Text);
                Paragraph(DescribeVariable(context, command.Name, isString: true));
                Paragraph(Docs.Get(command.Kind == CommandKind.LoadStringVariable ? "access.L$" : "access.S$"));
                break;

            case CommandKind.LoadSystemVariable or CommandKind.StoreSystemVariable:
                Header(command.Text);
                Paragraph(Docs.Get("sysvar." + CanonicalSystemVariable(context, command.Name)));
                Paragraph(Docs.Get(command.Kind == CommandKind.LoadSystemVariable ? "access.LS" : "access.SS"));
                break;

            case CommandKind.MacroCall:
                Header(command.Text);
                Paragraph(Docs.Get("access.ML"));
                Paragraph(DescribeMacroCall(context, command, index));
                break;

            case CommandKind.SystemMacroCall:
                {
                    Header(command.Text);
                    var macro = SystemMacroCatalog.Find(command.Name);
                    Paragraph(macro is null ? Docs.Get("access.MV") : Docs.Get(macro.DocumentationKey));
                    if (macro is not null)
                        Effect(macro.Effect);

                    break;
                }

            case CommandKind.Constant:
                Header(command.Text);
                Paragraph(Docs.Get("access.CL"));
                Paragraph(DescribeConstant(context, command.Name));
                break;

            case CommandKind.Curve:
                Header(command.Text);
                Paragraph(Docs.Get("access.FL"));
                Paragraph(DescribeCurve(context, command.Name));
                break;

            case CommandKind.SoundTrigger:
                Header(command.Text);
                Paragraph(Docs.Get("access.TL"));
                break;

            case CommandKind.SoundTriggerFile:
                Header(command.Text);
                Paragraph(Docs.Get("access.TF"));
                break;
        }
    }

    private static string CanonicalSystemVariable(FeatureContext context, string name)
    {
        var list = context.Analyses.FirstOrDefault()?.Context.SystemVariables ?? GameData.Default.SystemVariableList;
        var index = list.IndexOf(name);
        return index < 0 ? name : list.Names[index];
    }

    private static string DescribeVariable(FeatureContext context, string name, bool isString)
    {
        var entries = new List<(string Owner, string Text)>();
        foreach (var analysis in context.Analyses)
        {
            var table = isString ? analysis.Context.StringVariables : analysis.Context.FloatVariables;
            var definition = table.Find(name);
            var parts = new List<string>();
            if (Docs.Variable(analysis.Context.Kind, definition?.Name ?? name, isString) is { } summary)
                parts.Add(summary);

            if (definition is { Origin: VariableOrigin.VarList })
                parts.Add($"Declared in `{Path.GetFileName(definition.Path)}{definition.Line + 1}`.");
            else if (definition is null && analysis.Context.HasProject)
                parts.Add("Not declared.");

            entries.Add((analysis.Context.DisplayName, string.Join("\n\n", parts)));
        }

        return Scoped(entries);
    }

    private static string Scoped(List<(string Owner, string Text)> entries)
    {
        var distinct = entries.Select(e => e.Text).Distinct().ToList();
        if (distinct.Count <= 1)
            return distinct.FirstOrDefault() ?? string.Empty;

        return string.Join("\n\n", entries.GroupBy(e => e.Text).Select(g => $"{g.Key} *({string.Join(", ", g.Select(e => e.Owner))})*"));
    }

    private static string DescribeMacroCall(FeatureContext context, Command command, int index)
    {
        var lines = new List<string>();
        foreach (var analysis in context.Analyses)
        {
            if (analysis.ResolveMacroCall(context.Path, index) is not { } definition)
                continue;

            var source = context.GetSource(definition.Path);
            var line = source?.GetPosition(definition.HeaderSpan.Start).Line + 1;
            lines.Add($"Defined in `{Path.GetFileName(definition.Path)}:{line}`.");
        }

        return string.Join("\n\n", lines.Distinct());
    }

    private static string DescribeConstant(FeatureContext context, string name)
    {
        var entries = new List<(string Owner, string Text)>();
        foreach (var analysis in context.Analyses)
        {
            var constant = analysis.Context.Constants.Find(name);
            entries.Add((analysis.Context.DisplayName, constant is null
                ? analysis.Context.HasProject ? "Not defined." : string.Empty
                : $"Value: `{constant.Value.ToString("R", CultureInfo.InvariantCulture)}`. Defined in `{Path.GetFileName(constant.Path)}:{constant.Line + 1}`."));
        }

        return Scoped(entries);
    }

    private static string DescribeCurve(FeatureContext context, string name)
    {
        foreach (var analysis in context.Analyses)
        {
            if (analysis.Context.Curves.Find(name) is not { } curve)
                continue;

            var builder = new StringBuilder();
            builder.Append($"Defined in `{Path.GetFileName(curve.Path)}:{curve.Line + 1}`.\n\n");
            if (curve.Points.Count > 0)
            {
                builder.Append("| x | y |\n|---:|---:|\n");
                foreach (var point in curve.Points.Take(12))
                    builder.Append(CultureInfo.InvariantCulture, $"| {point.X:R} | {point.Y:R} |\n");
                if (curve.Points.Count > 12)
                    builder.Append($"| … | {curve.Points.Count - 12} more |\n");
            }

            return builder.ToString();
        }

        return context.Analyses.Any(a => a.Context.HasProject) ? "Not defined." : string.Empty;
    }

    private static string UsageSummary(FeatureContext context, SymbolKind kind, string name)
    {
        var calls = context.Analyses
            .SelectMany(a => a.Occurrences)
            .Where(o => o.Kind == kind && o.Role != SymbolRole.Definition && AsciiIgnoreCaseComparer.Instance.Equals(o.Name, name))
            .DistinctBy(o => (PathComparer.Normalize(o.Path), o.Span))
            .Count();
        return calls switch
        {
            0 => "Not called.",
            1 => "1 reference",
            _ => $"{calls} references",
        };
    }

    private static string? TriggerSource(FeatureContext context, string name)
    {
        var key = name;
        foreach (var suffix in new[] { "_drag", "_off" })
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                key = key[..^suffix.Length];
        }

        if (Docs.Trigger(key) is { } engine)
            return engine;

        return KeyboardTriggers.IsBound(context.Workspace, context.Path, key)
            ? Docs.Format("trigger.keyboard", key)
            : null;
    }
}
