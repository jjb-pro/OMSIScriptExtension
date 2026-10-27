using OMSIScript.Text;

namespace OMSIScript.Project;

public sealed record VarListEntry(string Name, string Path, int Line);

public sealed class VarList
{
    private VarList(string path, bool exists, IReadOnlyList<VarListEntry> entries)
    {
        Path = path;
        Exists = exists;
        Entries = entries;
    }

    public string Path { get; }

    public bool Exists { get; }

    public IReadOnlyList<VarListEntry> Entries { get; }

    public static VarList Parse(string path, string? text)
    {
        if (text is null)
            return new VarList(path, false, []);

        var lines = TextFiles.SplitLines(text);
        var entries = new VarListEntry[lines.Count];
        for (var i = 0; i < lines.Count; i++)
            entries[i] = new VarListEntry(lines[i], path, i);

        return new VarList(path, true, entries);
    }
}
