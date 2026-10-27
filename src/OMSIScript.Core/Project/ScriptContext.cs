using OMSIScript.Language;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Project;

public enum VariableOrigin
{
    Predefined,
    VarList,
}

public sealed record VariableDefinition(string Name, VariableOrigin Origin, int Index, string Path, int Line);

public sealed class SymbolTable<T> where T : class
{
    private readonly Dictionary<string, T> _items = new(AsciiIgnoreCaseComparer.Instance);
    private readonly List<T> _ordered = [];

    public IReadOnlyList<T> Items => _ordered;

    public int Count => _ordered.Count;

    public bool TryAdd(string name, T item)
    {
        _ordered.Add(item);
        return _items.TryAdd(name, item);
    }

    public T? Find(string name) => _items.GetValueOrDefault(name);

    public bool Contains(string name) => _items.ContainsKey(name);
}

public sealed class ScriptContext
{
    public ScriptContext(ObjectFile? owner, ObjectKind kind, IReadOnlyList<ScriptDocument> scripts,
        IReadOnlyList<VarList> varLists, IReadOnlyList<VarList> stringVarLists, IReadOnlyList<ConstFile> constFiles)
    {
        Owner = owner;
        Kind = kind;
        Scripts = scripts;
        VarLists = varLists;
        StringVarLists = stringVarLists;
        ConstFiles = constFiles;

        var kindData = GameData.Default.For(kind);
        Macros = kindData.Macros;
        SystemVariables = GameData.Default.SystemVariableList;

        AddPredefined(FloatVariables, kindData.FloatVariables);
        AddPredefined(StringVariables, kindData.StringVariables);
        AddVarLists(FloatVariables, varLists, kindData.FloatVariables.Names.Count);
        AddVarLists(StringVariables, stringVarLists, kindData.StringVariables.Names.Count);

        foreach (var file in constFiles)
        {
            foreach (var constant in file.Constants)
                Constants.TryAdd(constant.Name, constant);
            foreach (var curve in file.Curves)
                Curves.TryAdd(curve.Name, curve);
        }
    }

    public ObjectFile? Owner { get; }

    public bool HasProject => Owner is not null;

    public ObjectKind Kind { get; }

    public IReadOnlyList<ScriptDocument> Scripts { get; }

    public IReadOnlyList<VarList> VarLists { get; }

    public IReadOnlyList<VarList> StringVarLists { get; }

    public IReadOnlyList<ConstFile> ConstFiles { get; }

    public NameList Macros { get; }

    public NameList SystemVariables { get; }

    public SymbolTable<VariableDefinition> FloatVariables { get; } = new();

    public SymbolTable<VariableDefinition> StringVariables { get; } = new();

    public SymbolTable<ConstantDefinition> Constants { get; } = new();

    public SymbolTable<CurveDefinition> Curves { get; } = new();

    public string DisplayName => Owner is null ? "standalone script" : System.IO.Path.GetFileName(Owner.Path);

    private static void AddPredefined(SymbolTable<VariableDefinition> table, NameList list)
    {
        for (var i = 0; i < list.Names.Count; i++)
            table.TryAdd(list.Names[i], new VariableDefinition(list.Names[i], VariableOrigin.Predefined, i, string.Empty, i));
    }

    private static void AddVarLists(SymbolTable<VariableDefinition> table, IReadOnlyList<VarList> lists, int index)
    {
        foreach (var list in lists)
        {
            foreach (var entry in list.Entries)
                table.TryAdd(entry.Name, new VariableDefinition(entry.Name, VariableOrigin.VarList, index++, entry.Path, entry.Line));
        }
    }
}
