using OMSIScript.Features;

namespace OMSIScript.Core.Tests;

public sealed class FormatterTests
{
    private static string Format(string text, FormattingOptions? options = null)
    {
        var document = TestScripts.Parse(text);
        var result = text;
        foreach (var change in Formatter.Format(document, options ?? new FormattingOptions()).OrderByDescending(c => c.Span.Start))
            result = string.Concat(result.AsSpan(0, change.Span.Start), change.NewText, result.AsSpan(change.Span.End));
        return result;
    }

    [Fact]
    public void IndentsBlocksAndConditions()
    {
        var input = "{frame}\n1\n{if}\n2\n{else}\n3\n{endif}\n{end}";
        Assert.Equal("{frame}\n\t1\n\t{if}\n\t\t2\n\t{else}\n\t\t3\n\t{endif}\n{end}", Format(input));
    }

    [Fact]
    public void KeepsCommentsInFirstColumn()
    {
        var input = "{frame}\n'comment\n    1\n{end}";
        Assert.Equal("{frame}\n'comment\n\t1\n{end}", Format(input));
    }

    [Fact]
    public void LeavesMultiLineStringsUntouched()
    {
        var input = "{frame}\n\"a  \n    b\"\n{end}";
        Assert.Equal("{frame}\n\t\"a  \n    b\"\n{end}", Format(input));
    }

    [Fact]
    public void UsesSpacesWhenRequested()
    {
        Assert.Equal("{frame}\n  1\n{end}", Format("{frame}\n1\n{end}", new FormattingOptions(InsertSpaces: true, TabSize: 2)));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = Format("{macro:a}\n  1 {if} 2\n{endif}   \n{end}\n");
        Assert.Equal(once, Format(once));
    }
}
