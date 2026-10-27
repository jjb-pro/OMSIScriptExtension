using OMSIScript.Analysis;
using OMSIScript.Project;
using Xunit.Abstractions;

namespace OMSIScript.Core.Tests;

public sealed class WorkspaceTests(ITestOutputHelper output)
{
    private sealed class OpenFiles : ITextProvider
    {
        public Dictionary<string, string> Texts { get; } = new(Text.PathComparer.Instance);

        public string? GetOpenText(string path) => Texts.GetValueOrDefault(path);
    }

    private static string Fixtures([System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "client", "vscode", "test", "fixtures"));

    [Fact]
    public void ResolvesProjectOfFixture()
    {
        var root = Fixtures();
        var main = Path.Combine(root, "Vehicles", "Demo", "script", "main.osc");
        var files = new OpenFiles();
        files.Texts[main] = File.ReadAllText(main);

        var workspace = new Workspace(files);
        workspace.AddRoot(root);
        workspace.IndexPendingRoots();

        var owner = Assert.Single(workspace.GetOwners(main));
        foreach (var reference in owner.References)
            output.WriteLine($"{reference.Section} {reference.RawPath} -> {reference.FullPath} exists={workspace.Exists(reference.FullPath)}");

        var context = Assert.Single(workspace.GetContexts(main));
        Assert.Equal(2, context.Scripts.Count);
        Assert.True(context.Constants.Contains("speed"));
        Assert.True(context.FloatVariables.Contains("speed_var"));

        var analysis = Analyzer.Analyze(context);
        Assert.Equal([DiagnosticCodes.UnknownFloatVariable.Code], analysis.DiagnosticsFor(main).Select(d => d.Code));
    }
}
