using OMSIScript.Analysis;
using OMSIScript.Project;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record Location(string Path, TextSpan Span, SourceText? Source = null);

public sealed class FeatureContext
{
    public FeatureContext(Workspace workspace, string path, IReadOnlyList<ContextAnalysis> analyses)
    {
        Workspace = workspace;
        Path = path;
        Analyses = analyses;
        Document = analyses.Select(a => a.GetFile(path)?.Document).FirstOrDefault(d => d is not null) ?? workspace.GetDocument(path);
    }

    public Workspace Workspace { get; }

    public string Path { get; }

    public IReadOnlyList<ContextAnalysis> Analyses { get; }

    public ScriptDocument Document { get; }

    public FileStructure? Structure => Analyses.Select(a => a.GetFile(Path)).FirstOrDefault(f => f is not null);

    public (Command Command, int Index)? CommandAt(int offset)
    {
        var index = Document.FindCommandIndex(offset);
        return index < 0 ? null : (Document.Commands[index], index);
    }

    public SourceText? GetSource(string path)
    {
        if (PathComparer.Instance.Equals(path, Path))
            return Document.Source;

        foreach (var analysis in Analyses)
        {
            if (analysis.GetFile(path) is { } file)
                return file.Document.Source;
        }

        return Workspace.ReadText(path) is { } text ? new SourceText(text) : null;
    }

    public Location? LineLocation(string path, int line)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var source = GetSource(path);
        if (source is null || line >= source.LineCount)
            return null;

        return new Location(path, source.GetLineSpan(line), source);
    }
}
