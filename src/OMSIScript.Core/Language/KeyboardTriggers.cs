using OMSIScript.Project;
using OMSIScript.Text;

namespace OMSIScript.Language;

public static class KeyboardTriggers
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(PathComparer.Instance);

    public static IReadOnlyList<string> EngineTriggers { get; } = LoadEmbedded("engine_triggers.txt");

    private static IReadOnlyList<string> Embedded { get; } = LoadEmbedded("keyboard_vehicles.txt");

    public static IReadOnlyList<string> GetVehicleTriggers(Workspace workspace, string path)
    {
        var root = workspace.GetGameRoot(path);
        if (root is null)
            return Embedded;

        lock (Sync)
        {
            if (Cache.TryGetValue(root, out var cached))
                return cached;

            var text = TextFiles.TryRead(Path.Combine(root, "Inputs", "keyboard.cfg"));
            var names = text is null ? Embedded : Parse(text);
            Cache[root] = names;
            return names;
        }
    }

    public static bool IsBound(Workspace workspace, string path, string name)
        => GetVehicleTriggers(workspace, path).Contains(name, AsciiIgnoreCaseComparer.Instance);

    public static IReadOnlyList<string> Parse(string text)
    {
        var lines = TextFiles.SplitLines(text);
        var names = new List<string>();
        var inVehicles = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('[') && line != "[entry]")
                inVehicles = line == "[vehicles]";
            else if (inVehicles && line == "[entry]" && i + 1 < lines.Count)
                names.Add(lines[++i]);
        }

        return names;
    }

    private static IReadOnlyList<string> LoadEmbedded(string file)
    {
        using var stream = typeof(KeyboardTriggers).Assembly.GetManifestResourceStream("OMSIScript.Game." + file);
        if (stream is null)
            return [];

        using var reader = new StreamReader(stream);
        return [.. TextFiles.SplitLines(reader.ReadToEnd()).Where(l => l.Length > 0)];
    }
}
