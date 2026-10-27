using OMSIScript.Analysis;
using OMSIScript.Language;
using OMSIScript.Project;
using OMSIScript.Syntax;

namespace OMSIScript.Core.Tests;

internal static class TestScripts
{
    public static ScriptDocument Parse(string text, string path = "test.osc") => ScriptDocument.Parse(path, text);

    public static ContextAnalysis Analyze(params string[] scripts) => Analyze(ObjectKind.Vehicle, [], scripts);

    public static ContextAnalysis Analyze(ObjectKind kind, string[] variables, params string[] scripts)
    {
        var owner = ObjectFile.Parse("test.bus", "[script]\r\n0\r\n");
        var documents = scripts.Select((s, i) => ScriptDocument.Parse($"script{i}.osc", s)).ToList();
        var varList = VarList.Parse("vars.txt", string.Join("\r\n", variables));
        var constants = ConstFile.Parse("const.txt", "[const]\r\nspeed\r\n1.5\r\n[newcurve]\r\ncurve\r\n[pnt]\r\n0\r\n0\r\n[pnt]\r\n10\r\n100\r\n");
        var context = new ScriptContext(owner, kind, documents, [varList], [VarList.Parse("strings.txt", "text")], [constants]);
        return Analyzer.Analyze(context);
    }

    public static IReadOnlyList<string> Codes(this ContextAnalysis analysis) => [.. analysis.Diagnostics.Select(d => d.Code)];
}
