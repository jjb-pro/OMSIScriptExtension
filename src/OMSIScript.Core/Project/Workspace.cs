using OMSIScript.Analysis;
using OMSIScript.Language;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Project;

public interface ITextProvider
{
    string? GetOpenText(string path);
}

public sealed class Workspace(ITextProvider textProvider)
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "maps", "texture", "sound", "model", "screenshots", "fonts", "languages", "splines", "plugins", "node_modules", ".git", ".vs",
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, ObjectFile> _objects = new(PathComparer.Instance);
    private readonly Dictionary<string, List<string>> _owners = new(PathComparer.Instance);
    private readonly Dictionary<string, List<string>> _dependents = new(PathComparer.Instance);
    private readonly Dictionary<string, (string Text, ScriptDocument Document)> _documents = new(PathComparer.Instance);
    private readonly Dictionary<string, (DateTime Stamp, string? Text)> _diskFiles = new(PathComparer.Instance);
    private readonly HashSet<string> _roots = new(PathComparer.Instance);
    private readonly HashSet<string> _indexedRoots = new(PathComparer.Instance);
    private string? _gamePathOverride;

    public event Action? IndexChanged;

    public IReadOnlyCollection<ObjectFile> Objects
    {
        get
        {
            lock (_sync)
                return [.. _objects.Values];
        }
    }

    public string? GamePathOverride
    {
        get => _gamePathOverride;
        set => _gamePathOverride = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public void AddRoot(string root)
    {
        lock (_sync)
            _roots.Add(Path.GetFullPath(root));
    }

    public IReadOnlyList<string> PendingRoots()
    {
        lock (_sync)
            return [.. _roots.Where(r => !_indexedRoots.Contains(r))];
    }

    public void IndexPendingRoots(CancellationToken cancellation = default)
    {
        foreach (var root in PendingRoots())
        {
            var files = EnumerateObjectFiles(root, cancellation).ToList();
            var parsed = new List<ObjectFile>();
            foreach (var file in files)
            {
                cancellation.ThrowIfCancellationRequested();
                if (ObjectFile.Load(file) is { } objectFile)
                    parsed.Add(objectFile);
            }

            lock (_sync)
            {
                foreach (var objectFile in parsed)
                    SetObject(objectFile);
                _indexedRoots.Add(root);
            }
        }

        IndexChanged?.Invoke();
    }

    public void EnsureRootFor(string scriptPath)
    {
        var gameRoot = GameInstallation.FindRoot(scriptPath);
        string? root = null;
        if (gameRoot is not null)
        {
            var relative = Path.GetRelativePath(gameRoot, scriptPath);
            var top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            root = Path.Combine(gameRoot, top);
            if (top.Equals("Scripts", StringComparison.OrdinalIgnoreCase))
                root = gameRoot;
        }

        root ??= Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)) ?? Path.GetDirectoryName(scriptPath);
        if (root is null)
            return;

        lock (_sync)
        {
            if (_roots.Any(r => IsUnder(scriptPath, r)))
                return;
            _roots.Add(root);
        }
    }

    public void OnFileChanged(string path, bool deleted)
    {
        lock (_sync)
        {
            _diskFiles.Remove(path);
            _documents.Remove(path);

            if (ObjectKinds.IsObjectFile(path))
            {
                RemoveObject(path);
                var objectFile = deleted || !_roots.Any(r => IsUnder(path, r))
                    ? null
                    : textProvider.GetOpenText(path) is { } open ? ObjectFile.Parse(path, open) : ObjectFile.Load(path);
                if (objectFile is not null)
                    SetObject(objectFile);
            }
        }

        if (ObjectKinds.IsObjectFile(path))
            IndexChanged?.Invoke();
    }

    public IReadOnlyList<ObjectFile> GetOwners(string scriptPath)
    {
        lock (_sync)
        {
            return _owners.TryGetValue(scriptPath, out var owners)
                ? [.. owners.Select(o => _objects[o])]
                : [];
        }
    }

    public IReadOnlyList<ObjectFile> GetDependents(string path)
    {
        lock (_sync)
        {
            return _dependents.TryGetValue(path, out var owners)
                ? [.. owners.Select(o => _objects[o])]
                : [];
        }
    }

    public ObjectFile? GetObject(string path)
    {
        lock (_sync)
            return _objects.GetValueOrDefault(path);
    }

    public ScriptDocument GetDocument(string path)
    {
        var text = textProvider.GetOpenText(path) ?? ReadDisk(path) ?? string.Empty;
        lock (_sync)
        {
            if (_documents.TryGetValue(path, out var cached) && string.Equals(cached.Text, text, StringComparison.Ordinal))
                return cached.Document;

            var document = ScriptDocument.Parse(path, text);
            _documents[path] = (text, document);
            return document;
        }
    }

    public bool Exists(string path) => textProvider.GetOpenText(path) is not null || ReadDisk(path) is not null;

    public string? GetGameRoot(string path)
        => _gamePathOverride is { } configured && GameInstallation.IsRoot(configured) ? configured : GameInstallation.FindRoot(path);

    public IReadOnlyList<ScriptContext> GetContexts(string scriptPath)
    {
        var owners = GetOwners(scriptPath);
        if (owners.Count == 0)
            return [CreateStandaloneContext(scriptPath)];

        return [.. owners.Select(CreateContext)];
    }

    public ScriptContext CreateContext(ObjectFile owner)
    {
        var scripts = owner.Scripts.Where(r => Exists(r.FullPath)).Select(r => GetDocument(r.FullPath)).ToList();
        var varLists = owner.VarLists.Select(r => VarList.Parse(r.FullPath, ReadText(r.FullPath))).ToList();
        var stringVarLists = owner.StringVarLists.Select(r => VarList.Parse(r.FullPath, ReadText(r.FullPath))).ToList();
        var constFiles = owner.ConstFiles.Select(r => ConstFile.Parse(r.FullPath, ReadText(r.FullPath, detectUtf8: true))).ToList();
        return new ScriptContext(owner, owner.Kind, scripts, varLists, stringVarLists, constFiles);
    }

    public ScriptContext CreateStandaloneContext(string scriptPath)
        => new(null, GuessKind(scriptPath), [GetDocument(scriptPath)], [], [], []);

    public IReadOnlyList<ContextAnalysis> Analyze(string scriptPath) => [.. GetContexts(scriptPath).Select(Analyzer.Analyze)];

    public string? ReadText(string path, bool detectUtf8 = false)
        => textProvider.GetOpenText(path) ?? ReadDisk(path, detectUtf8);

    public static ObjectKind GuessKind(string scriptPath)
    {
        var segments = scriptPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments.Reverse())
        {
            if (segment.Equals("Sceneryobjects", StringComparison.OrdinalIgnoreCase))
                return ObjectKind.SceneryObject;
            if (segment.Equals("Vehicles", StringComparison.OrdinalIgnoreCase))
                return ObjectKind.Vehicle;
            if (segment.Equals("Humans", StringComparison.OrdinalIgnoreCase))
                return ObjectKind.Human;
        }

        return ObjectKind.Unknown;
    }

    private string? ReadDisk(string path, bool detectUtf8 = false)
    {
        DateTime stamp;
        try
        {
            if (!File.Exists(path))
                return null;
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return null;
        }

        lock (_sync)
        {
            if (_diskFiles.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Text;
        }

        var text = TextFiles.TryRead(path, detectUtf8);
        lock (_sync)
            _diskFiles[path] = (stamp, text);
        return text;
    }

    private void SetObject(ObjectFile objectFile)
    {
        RemoveObject(objectFile.Path);
        _objects[objectFile.Path] = objectFile;
        foreach (var reference in objectFile.References)
        {
            var map = reference.Section == ObjectSection.Script ? _owners : _dependents;
            if (!map.TryGetValue(reference.FullPath, out var list))
                map[reference.FullPath] = list = [];
            if (!list.Contains(objectFile.Path, PathComparer.Instance))
                list.Add(objectFile.Path);
        }
    }

    private void RemoveObject(string path)
    {
        if (!_objects.Remove(path, out var previous))
            return;

        foreach (var reference in previous.References)
        {
            var map = reference.Section == ObjectSection.Script ? _owners : _dependents;
            if (map.TryGetValue(reference.FullPath, out var list))
            {
                list.RemoveAll(p => PathComparer.Instance.Equals(p, path));
                if (list.Count == 0)
                    map.Remove(reference.FullPath);
            }
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static IEnumerable<string> EnumerateObjectFiles(string root, CancellationToken cancellation)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.System };

        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory, "*", options).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    if (!SkippedDirectories.Contains(Path.GetFileName(entry)))
                        pending.Push(entry);
                }
                else if (ObjectKinds.IsObjectFile(entry))
                {
                    yield return entry;
                }
            }
        }
    }
}
