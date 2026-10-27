using System.Globalization;
using OMSIScript.Text;

namespace OMSIScript.Analysis;

public enum Severity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

public enum DiagnosticTag
{
    None = 0,
    Unnecessary = 1,
    Deprecated = 2,
}

public sealed record DiagnosticDescriptor(string Code, Severity DefaultSeverity, string MessageFormat, DiagnosticTag Tag = DiagnosticTag.None)
{
    public string Format(object[] args) => string.Format(CultureInfo.InvariantCulture, MessageFormat, args);
}

public sealed record ScriptDiagnostic(string Path, TextSpan Span, Severity Severity, DiagnosticDescriptor Descriptor, string Message, string? Fix = null)
{
    public string Code => Descriptor.Code;

    public DiagnosticTag Tag => Descriptor.Tag;
}

public static class DiagnosticCodes
{
    public static readonly DiagnosticDescriptor InvalidCommand = new("OSC1001", Severity.Error, "Unknown command '{0}'.");
    public static readonly DiagnosticDescriptor DecimalComma = new("OSC1002", Severity.Error, "'{0}' is not a valid number. Use '.' as the decimal separator.");
    public static readonly DiagnosticDescriptor InvalidRegister = new("OSC1003", Severity.Error, "'{0}' is not a valid register. Use l0–l9 or s0–s9.");
    public static readonly DiagnosticDescriptor MisplacedComment = new("OSC1004", Severity.Error, "Comments must start at the beginning of a line.");
    public static readonly DiagnosticDescriptor UnterminatedString = new("OSC1005", Severity.Error, "Unterminated string literal.");
    public static readonly DiagnosticDescriptor CaseMismatch = new("OSC1006", Severity.Error, "Unknown command '{0}'. Did you mean '{1}'?");
    public static readonly DiagnosticDescriptor UnknownStringOperator = new("OSC1010", Severity.Error, "Unknown string operator '{0}'.{1}", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor InvalidPrefix = new("OSC1011", Severity.Error, "Unknown command prefix '{0}'.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor NonCanonicalPrefix = new("OSC1012", Severity.Warning, "Use '{0}' instead of '{1}'.");
    public static readonly DiagnosticDescriptor CurvePrefix = new("OSC1013", Severity.Error, "Use '(F.L.{0})' to evaluate the curve.");

    public static readonly DiagnosticDescriptor UnknownFloatVariable = new("OSC2001", Severity.Error, "The variable '{0}' is not declared in {1}.");
    public static readonly DiagnosticDescriptor UnknownStringVariable = new("OSC2002", Severity.Error, "The string variable '{0}' is not declared in {1}.");
    public static readonly DiagnosticDescriptor UnknownSystemVariable = new("OSC2003", Severity.Error, "Unknown system variable '{0}'.");
    public static readonly DiagnosticDescriptor UnknownConstant = new("OSC2004", Severity.Error, "The constant '{0}' is not defined in {1}.");
    public static readonly DiagnosticDescriptor UnknownCurve = new("OSC2005", Severity.Error, "The curve '{0}' is not defined in {1}.");
    public static readonly DiagnosticDescriptor UnknownMacro = new("OSC2006", Severity.Error, "The macro '{0}' is not defined in {1}.");
    public static readonly DiagnosticDescriptor MacroDefinedBeforeCall = new("OSC2007", Severity.Error, "The macro '{0}' must be defined after this call.");
    public static readonly DiagnosticDescriptor UnknownSystemMacro = new("OSC2008", Severity.Error, "Unknown system macro '{0}'.");
    public static readonly DiagnosticDescriptor SystemMacroUnavailable = new("OSC2009", Severity.Error, "The system macro '{0}' is not available for {1}s.");
    public static readonly DiagnosticDescriptor RecursiveMacro = new("OSC2010", Severity.Error, "The macro '{0}' cannot call itself.");
    public static readonly DiagnosticDescriptor MacroNotInFile = new("OSC2011", Severity.Information, "The macro '{0}' is not defined in this script.");

    public static readonly DiagnosticDescriptor ElseWithoutEndIf = new("OSC3001", Severity.Error, "'{{else}}' has no matching '{{endif}}'.");
    public static readonly DiagnosticDescriptor IfWithoutEndIf = new("OSC3002", Severity.Error, "'{{if}}' has no matching '{{endif}}'.");
    public static readonly DiagnosticDescriptor EndIfWithoutIf = new("OSC3003", Severity.Error, "'{{endif}}' has no matching '{{if}}'.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor ConditionCrossesBlock = new("OSC3004", Severity.Error, "The condition is not closed within this block.");
    public static readonly DiagnosticDescriptor MultipleElse = new("OSC3005", Severity.Error, "A condition cannot have more than one '{{else}}'.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor CodeOutsideBlock = new("OSC3010", Severity.Error, "Unreachable code detected.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor MissingEnd = new("OSC3011", Severity.Error, "'{0}' has no matching '{{end}}'.");
    public static readonly DiagnosticDescriptor EndWithoutBlock = new("OSC3012", Severity.Error, "'{{end}}' has no matching block header.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor DuplicateEntryPoint = new("OSC3013", Severity.Error, "'{0}' is already defined.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor DuplicateMacro = new("OSC3014", Severity.Error, "The macro '{0}' is redefined at {1}.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor DuplicateTrigger = new("OSC3015", Severity.Error, "The trigger '{0}' is redefined at {1}.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor EmptyName = new("OSC3016", Severity.Error, "The {0} name cannot be empty.");
    public static readonly DiagnosticDescriptor UnusedMacro = new("OSC3017", Severity.Hint, "The macro '{0}' is never called.", DiagnosticTag.Unnecessary);

    public static readonly DiagnosticDescriptor RegisterNotWritten = new("OSC4001", Severity.Warning, "Use of unassigned register '{0}'.");
    public static readonly DiagnosticDescriptor SystemVariableWrite = new("OSC4002", Severity.Warning, "Assigning the system variable '{0}' affects all objects.");
    public static readonly DiagnosticDescriptor DebugCommand = new("OSC4003", Severity.Information, "Debug command '{0}'.");
    public static readonly DiagnosticDescriptor IntToStrEnhFormat = new("OSC4004", Severity.Error, "Invalid $IntToStrEnh format \"{0}\". Expected a fill character followed by a length.");
    public static readonly DiagnosticDescriptor RepeatTooLong = new("OSC4005", Severity.Error, "'$*' cannot create strings of 1000 or more characters.");

    public static readonly DiagnosticDescriptor MissingFile = new("OSC5001", Severity.Error, "The file '{0}' does not exist.");
    public static readonly DiagnosticDescriptor ConstFileError = new("OSC5002", Severity.Error, "{0}");
    public static readonly DiagnosticDescriptor ConstFileWarning = new("OSC5003", Severity.Warning, "{0}");
    public static readonly DiagnosticDescriptor ObjectFileError = new("OSC5004", Severity.Error, "{0}");
    public static readonly DiagnosticDescriptor VarListWhitespace = new("OSC5005", Severity.Warning, "The variable name '{0}' has leading or trailing whitespace.");
    public static readonly DiagnosticDescriptor DuplicateVariable = new("OSC5006", Severity.Warning, "The variable '{0}' is already declared at {1}.", DiagnosticTag.Unnecessary);
    public static readonly DiagnosticDescriptor DuplicateConstant = new("OSC5007", Severity.Error, "The {0} '{1}' is already defined at {2}.", DiagnosticTag.Unnecessary);

    public static IReadOnlyList<DiagnosticDescriptor> All { get; } =
    [
        InvalidCommand, DecimalComma, InvalidRegister, MisplacedComment, UnterminatedString, CaseMismatch, UnknownStringOperator,
        InvalidPrefix, NonCanonicalPrefix, CurvePrefix,
        UnknownFloatVariable, UnknownStringVariable, UnknownSystemVariable, UnknownConstant, UnknownCurve, UnknownMacro,
        MacroDefinedBeforeCall, UnknownSystemMacro, SystemMacroUnavailable, RecursiveMacro, MacroNotInFile,
        ElseWithoutEndIf, IfWithoutEndIf, EndIfWithoutIf, ConditionCrossesBlock, MultipleElse, CodeOutsideBlock, MissingEnd,
        EndWithoutBlock, DuplicateEntryPoint, DuplicateMacro, DuplicateTrigger, EmptyName, UnusedMacro,
        RegisterNotWritten, SystemVariableWrite, DebugCommand, IntToStrEnhFormat, RepeatTooLong,
        MissingFile, ConstFileError, ConstFileWarning, ObjectFileError, VarListWhitespace, DuplicateVariable, DuplicateConstant,
    ];
}
