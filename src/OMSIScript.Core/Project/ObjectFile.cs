using System.Globalization;
using OMSIScript.Language;
using OMSIScript.Text;

namespace OMSIScript.Project;

public enum ObjectSection
{
    Script,
    VarNameList,
    StringVarNameList,
    ConstFile,
}

public sealed record FileReference(ObjectSection Section, string RawPath, string FullPath, int Line);

public sealed record ObjectFileProblem(int Line, string Message);

public sealed class ObjectFile
{
    private static readonly Dictionary<string, ObjectSection> Sections = new(StringComparer.Ordinal)
    {
        ["[script]"] = ObjectSection.Script,
        ["[varnamelist]"] = ObjectSection.VarNameList,
        ["[stringvarnamelist]"] = ObjectSection.StringVarNameList,
        ["[constfile]"] = ObjectSection.ConstFile,
    };

    private ObjectFile(string path, IReadOnlyList<FileReference> references, IReadOnlyList<ObjectFileProblem> problems)
    {
        Path = path;
        Kind = ObjectKinds.FromObjectFile(path);
        References = references;
        Problems = problems;
    }

    public string Path { get; }

    public ObjectKind Kind { get; }

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public IReadOnlyList<FileReference> References { get; }

    public IReadOnlyList<ObjectFileProblem> Problems { get; }

    public IEnumerable<FileReference> Scripts => References.Where(r => r.Section == ObjectSection.Script);

    public IEnumerable<FileReference> VarLists => References.Where(r => r.Section == ObjectSection.VarNameList);

    public IEnumerable<FileReference> StringVarLists => References.Where(r => r.Section == ObjectSection.StringVarNameList);

    public IEnumerable<FileReference> ConstFiles => References.Where(r => r.Section == ObjectSection.ConstFile);

    public static ObjectFile Parse(string path, string text)
    {
        var lines = TextFiles.SplitLines(text);
        var directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var references = new List<FileReference>();
        var problems = new List<ObjectFileProblem>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!Sections.TryGetValue(lines[i], out var section))
                continue;

            var countLine = i + 1;
            if (countLine >= lines.Count || !int.TryParse(lines[countLine], NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var count))
            {
                problems.Add(new ObjectFileProblem(Math.Min(countLine, lines.Count - 1), $"'{lines[i]}' must be followed by the number of entries."));
                continue;
            }

            i = countLine;
            for (var n = 0; n < count && i + 1 < lines.Count; n++)
            {
                i++;
                var raw = lines[i].Trim();
                references.Add(new FileReference(section, raw, Resolve(directory, raw), i));
            }
        }

        return new ObjectFile(path, references, problems);
    }

    public static ObjectFile? Load(string path)
    {
        var text = TextFiles.TryRead(path);
        return text is null ? null : Parse(path, text);
    }

    public static string Resolve(string directory, string relative)
    {
        try
        {
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, relative.Replace('/', '\\').Replace('\\', System.IO.Path.DirectorySeparatorChar)));
        }
        catch (ArgumentException)
        {
            return System.IO.Path.Combine(directory, relative);
        }
    }
}
