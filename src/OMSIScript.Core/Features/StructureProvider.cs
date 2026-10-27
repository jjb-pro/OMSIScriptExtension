using OMSIScript.Analysis;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record OutlineEntry(string Name, string Detail, BlockKind Kind, TextSpan Span, TextSpan SelectionSpan);

public enum FoldKind
{
    Region,
    Comment,
}

public sealed record FoldEntry(int StartLine, int EndLine, FoldKind Kind);

public static class StructureProvider
{
    public static IReadOnlyList<OutlineEntry> GetOutline(FeatureContext context)
    {
        if (context.Structure is not { } structure)
            return [];

        return [.. structure.Blocks.Select(b => new OutlineEntry(
            b.Header.ScopeName.Length > 0 ? b.Header.ScopeName : b.Header.Text,
            b.Kind switch
            {
                BlockKind.Macro => "macro",
                BlockKind.Trigger => "trigger",
                _ => "entry point",
            },
            b.Kind,
            b.Span,
            b.Name.Length > 0 ? b.Header.NameSpan : b.Header.Span))];
    }

    public static IReadOnlyList<FoldEntry> GetFolding(FeatureContext context)
    {
        var source = context.Document.Source;
        var folds = new List<FoldEntry>();

        void Add(int startOffset, int endOffset, FoldKind kind)
        {
            var start = source.GetPosition(startOffset).Line;
            var end = source.GetPosition(endOffset).Line;
            if (end > start)
                folds.Add(new FoldEntry(start, end, kind));
        }

        if (context.Structure is { } structure)
        {
            foreach (var block in structure.Blocks)
            {
                if (block.End is { } end)
                    AddBranch(block.Span.Start, end.Span.Start);
                else
                    Add(block.Span.Start, block.Span.End, FoldKind.Region);
            }

            foreach (var condition in structure.Conditions)
            {
                if (condition.Else is { } elseCommand)
                {
                    AddBranch(condition.If.Span.Start, elseCommand.Span.Start);
                    AddBranch(elseCommand.Span.Start, condition.EndIf!.Span.Start);
                }
                else if (condition.EndIf is { } endIf)
                {
                    AddBranch(condition.If.Span.Start, endIf.Span.Start);
                }
            }
        }

        var comments = context.Document.Lex.CommentLines;
        for (var i = 0; i < comments.Count;)
        {
            var j = i;
            while (j + 1 < comments.Count && comments[j + 1] == comments[j] + 1)
                j++;
            if (j > i)
                folds.Add(new FoldEntry(comments[i], comments[j], FoldKind.Comment));
            i = j + 1;
        }

        return [.. folds.DistinctBy(f => (f.StartLine, f.EndLine))];

        void AddBranch(int startOffset, int nextOffset)
        {
            var start = source.GetPosition(startOffset).Line;
            var end = source.GetPosition(nextOffset).Line - 1;
            if (end > start)
                folds.Add(new FoldEntry(start, end, FoldKind.Region));
        }
    }
}
