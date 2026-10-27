using OMSIScript.Syntax;

namespace OMSIScript.Core.Tests;

public sealed class LexerTests
{
    [Fact]
    public void SplitsOnSpacesAndTabsOnly()
    {
        var document = TestScripts.Parse("{frame}\r\n\t1 2\t+ (S.L.x)\r\n{end}");
        Assert.Equal(["{frame}", "1", "2", "+", "(S.L.x)", "{end}"], document.Lex.Tokens.Select(t => t.Text));
    }

    [Fact]
    public void CommentsOnlyInFirstColumn()
    {
        var document = TestScripts.Parse("'comment\r\n  'indented\r\n1 'trailing");
        Assert.Equal([0], document.Lex.CommentLines);
        Assert.Equal(["'indented", "1", "'trailing"], document.Lex.Tokens.Select(t => t.Text));
        Assert.All(document.Commands.Where(c => c.Text.StartsWith('\'')), c => Assert.Equal(CommandKind.Invalid, c.Kind));
    }

    [Fact]
    public void StringsKeepSpacesAndSpanLines()
    {
        var document = TestScripts.Parse("\"a b\" \"multi\r\nline\" x");
        Assert.Equal(["\"a b\"", "\"multi line\"", "x"], document.Lex.Tokens.Select(t => t.Text));
        Assert.Equal("multi line", document.Commands[1].Name);
    }

    [Fact]
    public void QuoteInsideTokenDoesNotSplit()
    {
        var document = TestScripts.Parse("abc\"d e\"f");
        Assert.Single(document.Lex.Tokens);
        Assert.Equal(CommandKind.Invalid, document.Commands[0].Kind);
    }

    [Fact]
    public void ReportsUnterminatedString()
    {
        var document = TestScripts.Parse("\"open\r\n1 2");
        Assert.NotNull(document.Lex.UnterminatedString);
    }

    [Fact]
    public void CommentLinesInsideOpenStringAreSkipped()
    {
        var document = TestScripts.Parse("\"a\r\n'comment\r\nb\"");
        Assert.Equal("\"a b\"", Assert.Single(document.Lex.Tokens).Text);
    }

    [Theory]
    [InlineData("{if}", CommandKind.If)]
    [InlineData("{IF}", CommandKind.Invalid)]
    [InlineData("{macro:Name}", CommandKind.MacroHeader)]
    [InlineData("{macro:}", CommandKind.MacroHeader)]
    [InlineData("{trigger:bus_doorfront0}", CommandKind.TriggerHeader)]
    [InlineData("s9", CommandKind.StoreRegister)]
    [InlineData("l0", CommandKind.LoadRegister)]
    [InlineData("sx", CommandKind.InvalidRegister)]
    [InlineData("Sin", CommandKind.Invalid)]
    [InlineData("sin", CommandKind.Operator)]
    [InlineData("$SetLengthC", CommandKind.StringOperator)]
    [InlineData("$SetLengthM", CommandKind.IgnoredStringOperator)]
    [InlineData("$cutend", CommandKind.IgnoredStringOperator)]
    [InlineData("%stackdump%", CommandKind.Operator)]
    [InlineData("(L.L.x)", CommandKind.LoadFloatVariable)]
    [InlineData("(S.$.x)", CommandKind.StoreStringVariable)]
    [InlineData("(L.S.Timegap)", CommandKind.LoadSystemVariable)]
    [InlineData("(L.M.Timegap)", CommandKind.LoadSystemVariable)]
    [InlineData("(S.S.Timegap)", CommandKind.StoreSystemVariable)]
    [InlineData("(M.L.x)", CommandKind.MacroCall)]
    [InlineData("(M.V.STLock)", CommandKind.SystemMacroCall)]
    [InlineData("(M.X.y)", CommandKind.IgnoredAccess)]
    [InlineData("(C.L.x)", CommandKind.Constant)]
    [InlineData("(F.L.x)", CommandKind.Curve)]
    [InlineData("(T.L.x)", CommandKind.SoundTrigger)]
    [InlineData("(T.F.x)", CommandKind.SoundTriggerFile)]
    [InlineData("(X.L.x)", CommandKind.IgnoredAccess)]
    [InlineData("(L.L.x", CommandKind.Invalid)]
    [InlineData("1.5", CommandKind.Number)]
    [InlineData("-.5", CommandKind.Number)]
    [InlineData("5.", CommandKind.Number)]
    [InlineData("1e3", CommandKind.Number)]
    [InlineData("1,5", CommandKind.Invalid)]
    [InlineData("\"\"", CommandKind.StringLiteral)]
    public void ClassifiesLikeOmsi(string token, CommandKind expected)
        => Assert.Equal(expected, CommandClassifier.Classify(new RawToken(token, new Text.TextSpan(0, token.Length))).Kind);
}
