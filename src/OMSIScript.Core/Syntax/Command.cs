using OMSIScript.Language;
using OMSIScript.Text;

namespace OMSIScript.Syntax;

public enum CommandKind
{
    StringLiteral,
    Number,
    If,
    Else,
    EndIf,
    Init,
    Frame,
    FrameAI,
    End,
    MacroHeader,
    TriggerHeader,
    LoadRegister,
    StoreRegister,
    Operator,
    StringOperator,
    LoadFloatVariable,
    StoreFloatVariable,
    LoadStringVariable,
    StoreStringVariable,
    LoadSystemVariable,
    StoreSystemVariable,
    MacroCall,
    SystemMacroCall,
    Constant,
    Curve,
    SoundTrigger,
    SoundTriggerFile,
    IgnoredStringOperator,
    IgnoredAccess,
    InvalidRegister,
    Invalid,
}

public sealed class Command
{
    public required CommandKind Kind { get; init; }

    public required string Text { get; init; }

    public required TextSpan Span { get; init; }

    public string Name { get; init; } = string.Empty;

    public TextSpan NameSpan { get; init; }

    public int Register { get; init; } = -1;

    public float Value { get; init; }

    public OperatorInfo? Operator { get; init; }

    public bool IsBlockHeader => Kind is CommandKind.Init or CommandKind.Frame or CommandKind.FrameAI or CommandKind.MacroHeader or CommandKind.TriggerHeader;

    public string ScopeName => Kind switch
    {
        CommandKind.Init => "init",
        CommandKind.Frame => "frame",
        CommandKind.FrameAI => "frame_ai",
        CommandKind.MacroHeader or CommandKind.TriggerHeader => Name,
        _ => Text,
    };

    public bool IsEntryHeader => Kind is CommandKind.Init or CommandKind.Frame or CommandKind.FrameAI;

    public bool HasName => Kind is CommandKind.MacroHeader or CommandKind.TriggerHeader
        or CommandKind.LoadFloatVariable or CommandKind.StoreFloatVariable
        or CommandKind.LoadStringVariable or CommandKind.StoreStringVariable
        or CommandKind.LoadSystemVariable or CommandKind.StoreSystemVariable
        or CommandKind.MacroCall or CommandKind.SystemMacroCall
        or CommandKind.Constant or CommandKind.Curve
        or CommandKind.SoundTrigger or CommandKind.SoundTriggerFile;
}
