using OMSIScript.Analysis;
using OMSIScript.Language;

namespace OMSIScript.Core.Tests;

public sealed class AnalyzerTests
{
    [Fact]
    public void ValidScriptHasNoDiagnostics()
    {
        var analysis = TestScripts.Analyze(ObjectKind.Vehicle, ["x"],
            "{frame}\r\n\t(L.L.Velocity) (C.L.speed) * (F.L.curve) (S.L.x)\r\n\t(M.L.helper)\r\n{end}\r\n{macro:helper}\r\n\t\"a\" (S.$.text) $length s0 l0 {if} 1 {else} 2 {endif}\r\n{end}");
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void MacroMustBeDefinedAfterCall()
    {
        var analysis = TestScripts.Analyze("{macro:a}\r\n{end}\r\n{frame}\r\n(M.L.a)\r\n{end}");
        Assert.Contains(DiagnosticCodes.MacroDefinedBeforeCall.Code, analysis.Codes());
    }

    [Fact]
    public void MacroDefinedInLaterFileResolves()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n(M.L.a)\r\n{end}", "{macro:A}\r\n{end}");
        Assert.DoesNotContain(DiagnosticCodes.MacroDefinedBeforeCall.Code, analysis.Codes());
        Assert.DoesNotContain(DiagnosticCodes.UnknownMacro.Code, analysis.Codes());
        Assert.Equal("script1.osc", analysis.ResolveMacroCall("script0.osc", 1)!.Path);
    }

    [Fact]
    public void RecursionIsReported()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n(M.L.a)\r\n{end}\r\n{macro:a}\r\n(M.L.a)\r\n{end}");
        Assert.Contains(DiagnosticCodes.RecursiveMacro.Code, analysis.Codes());
    }

    [Fact]
    public void LastMacroDefinitionWins()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n(M.L.a)\r\n{end}\r\n{macro:a}\r\n{end}\r\n{macro:a}\r\n{end}");
        var duplicate = Assert.Single(analysis.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateMacro.Code);
        Assert.Equal(3, analysis.GetFile("script0.osc")!.Document.Source.GetPosition(duplicate.Span.Start).Line);
        Assert.Equal(5, analysis.ResolveMacroCall("script0.osc", 1)!.CommandIndex);
    }

    [Fact]
    public void FirstEntryPointWins()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n{end}", "{frame}\r\n{end}");
        var duplicate = Assert.Single(analysis.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateEntryPoint.Code);
        Assert.Equal("script1.osc", duplicate.Path);
    }

    [Fact]
    public void ConditionStackIsGlobal()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n1 {if}\r\n{end}\r\n{macro:a}\r\n{endif}\r\n{end}");
        Assert.Contains(DiagnosticCodes.ConditionCrossesBlock.Code, analysis.Codes());
        Assert.DoesNotContain(DiagnosticCodes.IfWithoutEndIf.Code, analysis.Codes());
    }

    [Theory]
    [InlineData("{frame} 1 {if} {end}", "OSC3002")]
    [InlineData("{frame} {else} {end}", "OSC3001")]
    [InlineData("{frame} {endif} {end}", "OSC3003")]
    [InlineData("{frame} 1 {if} {else} {else} {endif} {end}", "OSC3005")]
    [InlineData("1 {frame} {end}", "OSC3010")]
    [InlineData("{frame} {end} 1", "OSC3010")]
    [InlineData("{frame} {macro:a} {end}", "OSC3011")]
    [InlineData("{frame} {end} {end}", "OSC3012")]
    [InlineData("{frame} (L.L.unknown) {end}", "OSC2001")]
    [InlineData("{frame} (L.$.unknown) {end}", "OSC2002")]
    [InlineData("{frame} (L.S.unknown) {end}", "OSC2003")]
    [InlineData("{frame} (C.L.unknown) {end}", "OSC2004")]
    [InlineData("{frame} (F.L.unknown) {end}", "OSC2005")]
    [InlineData("{frame} (M.L.unknown) {end}", "OSC2006")]
    [InlineData("{frame} (M.V.unknown) {end}", "OSC2008")]
    [InlineData("{frame} (F.X.curve) {end}", "OSC1013")]
    [InlineData("{frame} (L.M.Timegap) {end}", "OSC1012")]
    [InlineData("{frame} 1 (S.S.Timegap) {end}", "OSC4002")]
    [InlineData("{frame} l3 {end}", "OSC4001")]
    [InlineData("{frame} \"5\" 1 $IntToStrEnh {end}", "OSC4004")]
    [InlineData("{frame} 1000 \"a\" $* {end}", "OSC4005")]
    [InlineData("{frame} 1,5 {end}", "OSC1002")]
    [InlineData("{frame} {If} {end}", "OSC1006")]
    [InlineData("{frame} sx {end}", "OSC1003")]
    [InlineData("{frame} $=> {end}", "OSC1010")]
    [InlineData("{frame} foo {end}", "OSC1001")]
    [InlineData("{frame} 1 'x {end}", "OSC1004")]
    [InlineData("{macro:} {end}", "OSC3016")]
    public void ReportsDiagnostic(string script, string code)
        => Assert.Contains(code, TestScripts.Analyze(script).Codes());

    [Fact]
    public void ScriptTextureMacrosAreUnavailableForSceneryObjects()
    {
        var analysis = TestScripts.Analyze(ObjectKind.SceneryObject, [], "{init}\r\n0 (M.V.STNewTex)\r\n{end}");
        Assert.Contains(DiagnosticCodes.SystemMacroUnavailable.Code, analysis.Codes());
        Assert.Empty(TestScripts.Analyze(ObjectKind.SceneryObject, [], "{frame}\r\n0 (M.V.GetArrBusLine)\r\n{end}").Diagnostics);
    }

    [Fact]
    public void RegistersZeroToNineAreValid()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n1 s9 l9\r\n{end}");
        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public void RegistersAreInheritedByMacros()
    {
        var analysis = TestScripts.Analyze("{frame}\r\n1 s0 (M.L.a)\r\n{end}\r\n{macro:a}\r\nl0\r\n{end}");
        Assert.DoesNotContain(DiagnosticCodes.RegisterNotWritten.Code, analysis.Codes());
    }

    [Fact]
    public void NamesAreCaseInsensitive()
    {
        var analysis = TestScripts.Analyze(ObjectKind.Vehicle, ["MyVar"], "{frame}\r\n(L.L.myvar) (L.L.VELOCITY) (L.S.timegap) (C.L.SPEED) (M.V.stlock)\r\n{end}");
        Assert.Empty(analysis.Diagnostics);
    }
}
