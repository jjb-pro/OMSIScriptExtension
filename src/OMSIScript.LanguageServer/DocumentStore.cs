using OMSIScript.Project;
using OMSIScript.Text;
using OMSIScript.LanguageServer.Protocol;

namespace OMSIScript.LanguageServer;

public sealed class OpenDocument(string uri, string path, int version, SourceText text)
{
    public string Uri { get; } = uri;

    public string Path { get; } = path;

    public int Version { get; set; } = version;

    public SourceText Text { get; set; } = text;
}

public sealed class DocumentStore : ITextProvider
{
    private readonly Dictionary<string, OpenDocument> _documents = new(PathComparer.Instance);
    private readonly Lock _sync = new();

    public IReadOnlyList<OpenDocument> All
    {
        get
        {
            lock (_sync)
                return [.. _documents.Values];
        }
    }

    public string? GetOpenText(string path)
    {
        lock (_sync)
            return _documents.TryGetValue(path, out var document) ? document.Text.Text : null;
    }

    public OpenDocument? Get(string path)
    {
        lock (_sync)
            return _documents.GetValueOrDefault(path);
    }

    public OpenDocument Open(string uri, int version, string text)
    {
        var path = Uris.ToPath(uri);
        var document = new OpenDocument(uri, path, version, new SourceText(text));
        lock (_sync)
            _documents[path] = document;
        return document;
    }

    public OpenDocument? Change(string uri, int version, IReadOnlyList<TextDocumentContentChangeEvent> changes)
    {
        lock (_sync)
        {
            if (!_documents.TryGetValue(Uris.ToPath(uri), out var document))
                return null;

            var text = document.Text;
            foreach (var change in changes)
            {
                text = change.Range is { } range
                    ? text.WithChange(Conversions.ToLineRange(range), change.Text)
                    : new SourceText(change.Text);
            }

            document.Text = text;
            document.Version = version;
            return document;
        }
    }

    public void Close(string uri)
    {
        lock (_sync)
            _documents.Remove(Uris.ToPath(uri));
    }

    public string UriFor(string path)
    {
        lock (_sync)
            return _documents.TryGetValue(path, out var document) ? document.Uri : Uris.FromPath(path);
    }
}

public static class Uris
{
    public static string ToPath(string uri)
    {
        if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return uri;

        var normalized = uri.Replace("%3A", ":", StringComparison.OrdinalIgnoreCase);
        if (!System.Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            return uri;

        return System.IO.Path.GetFullPath(parsed.LocalPath);
    }

    public static string FromPath(string path)
    {
        if (!System.IO.Path.IsPathRooted(path))
            return path;

        return new System.Uri(path).AbsoluteUri;
    }
}

public static class Conversions
{
    public static LineRange ToLineRange(Protocol.Range range)
        => new(new LinePosition(range.Start.Line, range.Start.Character), new LinePosition(range.End.Line, range.End.Character));

    public static Position ToPosition(LinePosition position) => new(position.Line, position.Character);

    public static Protocol.Range ToRange(SourceText source, TextSpan span)
    {
        var range = source.GetRange(span);
        return new Protocol.Range(ToPosition(range.Start), ToPosition(range.End));
    }

    public static int ToOffset(SourceText source, Position position) => source.GetOffset(new LinePosition(position.Line, position.Character));
}
