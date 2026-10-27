using OMSIScript.Analysis;
using OMSIScript.Project;
using Xunit.Abstractions;

namespace OMSIScript.Core.Tests;

public sealed class CorpusTests(ITestOutputHelper output)
{
    private sealed class DiskOnly : ITextProvider
    {
        public string? GetOpenText(string path) => null;
    }

    public static string? GameRoot => Environment.GetEnvironmentVariable("OMSI_GAME_PATH") is { Length: > 0 } configured
        ? configured
        : Directory.Exists(DefaultGameRoot) ? DefaultGameRoot : null;

    private const string DefaultGameRoot = @"C:\Program Files (x86)\Steam\steamapps\common\OMSI 2";

    [Fact]
    public void AnalyzesInstalledContent()
    {
        if (GameRoot is not { } root)
        {
            output.WriteLine("OMSI 2 installation not found; skipping corpus analysis.");
            return;
        }

        var workspace = new Workspace(new DiskOnly());
        foreach (var folder in new[] { "Vehicles", "Sceneryobjects", "Humans" })
            workspace.AddRoot(Path.Combine(root, folder));
        workspace.IndexPendingRoots();

        var counts = new Dictionary<string, int>();
        var samples = new Dictionary<string, List<string>>();
        var analyzed = 0;
        foreach (var owner in workspace.Objects)
        {
            var analysis = Analyzer.Analyze(workspace.CreateContext(owner));
            analyzed++;
            foreach (var diagnostic in analysis.Diagnostics)
            {
                var key = $"{diagnostic.Severity} {diagnostic.Code}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
                if (!samples.TryGetValue(key, out var list))
                    samples[key] = list = [];
                if (list.Count < 6)
                {
                    var document = analysis.Context.Scripts.First(s => s.Path == diagnostic.Path);
                    var position = document.Source.GetPosition(diagnostic.Span.Start);
                    list.Add($"{Path.GetRelativePath(root, diagnostic.Path)}:{position.Line + 1}  {diagnostic.Message}");
                }
            }
        }

        output.WriteLine($"Analyzed {analyzed} objects.");
        foreach (var (key, count) in counts.OrderBy(k => k.Key))
        {
            output.WriteLine($"{key}: {count}");
            foreach (var sample in samples[key])
                output.WriteLine("    " + sample);
        }

        Assert.True(analyzed > 0);
    }

    [Fact]
    public void FormattingPreservesTokensOfInstalledContent()
    {
        if (GameRoot is not { } root)
            return;

        var files = Directory.EnumerateFiles(root, "*.osc", SearchOption.AllDirectories).ToList();
        var changedFiles = 0;
        foreach (var file in files)
        {
            var text = Text.TextFiles.TryRead(file)!;
            var original = Syntax.ScriptDocument.Parse(file, text);
            var formatted = Apply(text, Features.Formatter.Format(original, new Features.FormattingOptions()));
            var reparsed = Syntax.ScriptDocument.Parse(file, formatted);

            Assert.True(original.Lex.Tokens.Select(t => t.Text).SequenceEqual(reparsed.Lex.Tokens.Select(t => t.Text)), $"Tokens changed in {file}");
            Assert.Equal(original.Lex.CommentLines.Count, reparsed.Lex.CommentLines.Count);
            Assert.Equal(formatted, Apply(formatted, Features.Formatter.Format(reparsed, new Features.FormattingOptions())));
            if (formatted != text)
                changedFiles++;
        }

        output.WriteLine($"Formatted {files.Count} files, {changedFiles} would change.");
    }

    private static string Apply(string text, IEnumerable<Features.TextChange> changes)
    {
        foreach (var change in changes.OrderByDescending(c => c.Span.Start))
            text = string.Concat(text.AsSpan(0, change.Span.Start), change.NewText, text.AsSpan(change.Span.End));
        return text;
    }
}
