using OMSIScript.Project;
using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Analysis;

public sealed class FileStructure(ScriptDocument document, IReadOnlyList<BlockInfo> blocks, IReadOnlyList<ConditionInfo> conditions)
{
    public ScriptDocument Document { get; } = document;

    public IReadOnlyList<BlockInfo> Blocks { get; } = blocks;

    public IReadOnlyList<ConditionInfo> Conditions { get; } = conditions;

    public BlockInfo? FindBlock(int offset) => Blocks.FirstOrDefault(b => b.Span.ContainsOrTouches(offset));
}

public sealed class ContextAnalysis
{
    private readonly Dictionary<string, FileStructure> _files;
    private readonly Dictionary<(string Path, int Command), MacroDefinition> _macroResolutions;

    internal ContextAnalysis(ScriptContext context, IReadOnlyList<ScriptDiagnostic> diagnostics, IReadOnlyList<SymbolOccurrence> occurrences,
        IReadOnlyList<MacroDefinition> macros, IReadOnlyList<MacroDefinition> triggers, Dictionary<string, FileStructure> files,
        Dictionary<(string Path, int Command), MacroDefinition> macroResolutions)
    {
        Context = context;
        Diagnostics = diagnostics;
        Occurrences = occurrences;
        Macros = macros;
        Triggers = triggers;
        _files = files;
        _macroResolutions = macroResolutions;
    }

    public ScriptContext Context { get; }

    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    public IReadOnlyList<SymbolOccurrence> Occurrences { get; }

    public IReadOnlyList<MacroDefinition> Macros { get; }

    public IReadOnlyList<MacroDefinition> Triggers { get; }

    public FileStructure? GetFile(string path) => _files.GetValueOrDefault(path);

    public IEnumerable<ScriptDiagnostic> DiagnosticsFor(string path) => Diagnostics.Where(d => PathComparer.Instance.Equals(d.Path, path));

    public MacroDefinition? ResolveMacroCall(string path, int commandIndex)
        => _macroResolutions.GetValueOrDefault((PathComparer.Normalize(path)!, commandIndex));

    public IEnumerable<MacroDefinition> FindMacroDefinitions(string name)
        => Macros.Where(m => AsciiIgnoreCaseComparer.Instance.Equals(m.Name, name));

    internal static (string, int) Key(string path, int command) => (PathComparer.Normalize(path)!, command);
}
