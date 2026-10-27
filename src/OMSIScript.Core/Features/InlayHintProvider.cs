using System.Globalization;
using OMSIScript.Syntax;

namespace OMSIScript.Features;

public sealed record InlayHintEntry(int Offset, string Label, string? Tooltip);

public static class InlayHintProvider
{
    public static IReadOnlyList<InlayHintEntry> GetHints(FeatureContext context, int startOffset, int endOffset)
    {
        var hints = new List<InlayHintEntry>();
        foreach (var command in context.Document.Commands)
        {
            if (command.Span.End < startOffset || command.Span.Start > endOffset || command.Kind != CommandKind.Constant)
                continue;

            var values = context.Analyses
                .Select(a => a.Context.Constants.Find(command.Name))
                .Where(c => c is not null)
                .Select(c => c!.Value)
                .Distinct()
                .ToList();

            if (values.Count == 0)
                continue;

            var label = values.Count == 1
                ? "= " + values[0].ToString("R", CultureInfo.InvariantCulture)
                : "= " + string.Join(" | ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            hints.Add(new InlayHintEntry(command.Span.End, label, values.Count > 1 ? "The constant has different values in the objects that use this script." : null));
        }

        return hints;
    }
}
