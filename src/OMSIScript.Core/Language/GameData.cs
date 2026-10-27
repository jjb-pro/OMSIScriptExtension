using OMSIScript.Text;

namespace OMSIScript.Language;

public sealed class NameList
{
    private readonly Dictionary<string, int> _index;

    public NameList(IReadOnlyList<string> names)
    {
        Names = names;
        _index = new Dictionary<string, int>(AsciiIgnoreCaseComparer.Instance);
        for (var i = 0; i < names.Count; i++)
            _index.TryAdd(names[i], i);
    }

    public IReadOnlyList<string> Names { get; }

    public int IndexOf(string name) => _index.GetValueOrDefault(name, -1);

    public bool Contains(string name) => _index.ContainsKey(name);
}

public sealed class GameData
{
    private static readonly NameList Empty = new([]);

    private GameData()
    {
        SystemVariableList = Load("varlist_system.txt");

        var textureMacros = Load("callbacklist_scripttex.txt");
        Vehicle = new KindData(Load("varlist_roadvehicle.txt"), Load("stringvarlist_roadvehicle.txt"),
            new NameList([.. Load("callbacklist_roadvehicle.txt").Names, .. textureMacros.Names, .. textureMacros.Names]));
        SceneryObject = new KindData(Load("varlist_scenobj.txt"), Empty, Load("callbacklist_scenobj.txt"));
        Human = new KindData(Load("varlist_human.txt"), Empty, Empty);
        Unknown = new KindData(
            new NameList([.. Vehicle.FloatVariables.Names, .. SceneryObject.FloatVariables.Names, .. Human.FloatVariables.Names]),
            Vehicle.StringVariables,
            new NameList([.. Vehicle.Macros.Names, .. SceneryObject.Macros.Names]));
    }

    public static GameData Default { get; } = new();

    public NameList SystemVariableList { get; }

    public KindData Vehicle { get; }

    public KindData SceneryObject { get; }

    public KindData Human { get; }

    public KindData Unknown { get; }

    public KindData For(ObjectKind kind) => kind switch
    {
        ObjectKind.Vehicle => Vehicle,
        ObjectKind.SceneryObject => SceneryObject,
        ObjectKind.Human => Human,
        _ => Unknown,
    };

    private static NameList Load(string file)
    {
        using var stream = typeof(GameData).Assembly.GetManifestResourceStream("OMSIScript.Game." + file)
            ?? throw new InvalidOperationException($"Missing resource '{file}'.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new NameList(TextFiles.SplitLines(TextFiles.Decode(memory.ToArray())));
    }

    public sealed record KindData(NameList FloatVariables, NameList StringVariables, NameList Macros);
}
