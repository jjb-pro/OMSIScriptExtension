using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Analysis;

public enum SymbolKind
{
    FloatVariable,
    StringVariable,
    SystemVariable,
    Macro,
    SystemMacro,
    Constant,
    Curve,
    Trigger,
    SoundTrigger,
    Register,
}

public enum SymbolRole
{
    Definition,
    Read,
    Write,
    Call,
}

public sealed record SymbolOccurrence(SymbolKind Kind, string Name, SymbolRole Role, string Path, TextSpan Span, int CommandIndex);

public sealed record MacroDefinition(string Name, string Path, int CommandIndex, int StreamIndex, TextSpan NameSpan, TextSpan HeaderSpan);

public enum BlockKind
{
    Init,
    Frame,
    FrameAI,
    Macro,
    Trigger,
}

public sealed record BlockInfo(BlockKind Kind, string Name, Command Header, int HeaderIndex, Command? End, int EndIndex, TextSpan Span);

public sealed record ConditionInfo(Command If, Command? Else, Command? EndIf);

public static class SymbolKinds
{
    public static SymbolKind? FromCommand(CommandKind kind) => kind switch
    {
        CommandKind.LoadFloatVariable or CommandKind.StoreFloatVariable => SymbolKind.FloatVariable,
        CommandKind.LoadStringVariable or CommandKind.StoreStringVariable => SymbolKind.StringVariable,
        CommandKind.LoadSystemVariable or CommandKind.StoreSystemVariable => SymbolKind.SystemVariable,
        CommandKind.MacroCall or CommandKind.MacroHeader => SymbolKind.Macro,
        CommandKind.SystemMacroCall => SymbolKind.SystemMacro,
        CommandKind.Constant => SymbolKind.Constant,
        CommandKind.Curve => SymbolKind.Curve,
        CommandKind.TriggerHeader => SymbolKind.Trigger,
        CommandKind.SoundTrigger or CommandKind.SoundTriggerFile => SymbolKind.SoundTrigger,
        CommandKind.LoadRegister or CommandKind.StoreRegister => SymbolKind.Register,
        _ => null,
    };

    public static SymbolRole RoleFromCommand(CommandKind kind) => kind switch
    {
        CommandKind.MacroHeader or CommandKind.TriggerHeader => SymbolRole.Definition,
        CommandKind.StoreFloatVariable or CommandKind.StoreStringVariable or CommandKind.StoreSystemVariable or CommandKind.StoreRegister => SymbolRole.Write,
        CommandKind.MacroCall or CommandKind.SystemMacroCall or CommandKind.SoundTrigger or CommandKind.SoundTriggerFile or CommandKind.Curve => SymbolRole.Call,
        _ => SymbolRole.Read,
    };

    public static string DisplayName(SymbolKind kind) => kind switch
    {
        SymbolKind.FloatVariable => "variable",
        SymbolKind.StringVariable => "string variable",
        SymbolKind.SystemVariable => "system variable",
        SymbolKind.Macro => "macro",
        SymbolKind.SystemMacro => "system macro",
        SymbolKind.Constant => "constant",
        SymbolKind.Curve => "curve",
        SymbolKind.Trigger => "trigger",
        SymbolKind.SoundTrigger => "sound trigger",
        SymbolKind.Register => "register",
        _ => "symbol",
    };
}
