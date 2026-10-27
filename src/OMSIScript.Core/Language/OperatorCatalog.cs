using System.Collections.Frozen;

namespace OMSIScript.Language;

public enum OperatorCategory
{
    Arithmetic,
    Comparison,
    Logical,
    Math,
    Stack,
    String,
    StringComparison,
    Conversion,
    Debug,
}

public sealed record StackEffect(int FloatsRead, int FloatsLeft, int StringsRead, int StringsLeft);

public sealed record OperatorInfo(string Token, string Id, OperatorCategory Category, StackEffect Effect)
{
    public string DocumentationKey => "op." + Id;
}

public static class OperatorCatalog
{
    public static IReadOnlyList<OperatorInfo> FloatOperators { get; } =
    [
        new("+", "add", OperatorCategory.Arithmetic, new(2, 1, 0, 0)),
        new("-", "sub", OperatorCategory.Arithmetic, new(2, 1, 0, 0)),
        new("*", "mul", OperatorCategory.Arithmetic, new(2, 1, 0, 0)),
        new("/", "div", OperatorCategory.Arithmetic, new(2, 1, 0, 0)),
        new("%", "mod", OperatorCategory.Arithmetic, new(2, 1, 0, 0)),
        new("/-/", "neg", OperatorCategory.Arithmetic, new(1, 1, 0, 0)),
        new("=", "eq", OperatorCategory.Comparison, new(2, 1, 0, 0)),
        new("<", "lt", OperatorCategory.Comparison, new(2, 1, 0, 0)),
        new(">", "gt", OperatorCategory.Comparison, new(2, 1, 0, 0)),
        new("<=", "le", OperatorCategory.Comparison, new(2, 1, 0, 0)),
        new(">=", "ge", OperatorCategory.Comparison, new(2, 1, 0, 0)),
        new("&&", "and", OperatorCategory.Logical, new(2, 1, 0, 0)),
        new("||", "or", OperatorCategory.Logical, new(2, 1, 0, 0)),
        new("!", "not", OperatorCategory.Logical, new(1, 1, 0, 0)),
        new("sin", "sin", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("arcsin", "arcsin", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("arctan", "arctan", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("min", "min", OperatorCategory.Math, new(2, 1, 0, 0)),
        new("max", "max", OperatorCategory.Math, new(2, 1, 0, 0)),
        new("exp", "exp", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("sqrt", "sqrt", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("sqr", "sqr", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("sgn", "sgn", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("abs", "abs", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("trunc", "trunc", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("random", "random", OperatorCategory.Math, new(1, 1, 0, 0)),
        new("pi", "pi", OperatorCategory.Math, new(0, 1, 0, 0)),
        new("d", "dup", OperatorCategory.Stack, new(1, 2, 0, 0)),
        new("%stackdump%", "stackdump", OperatorCategory.Debug, new(8, 8, 0, 0)),
    ];

    public static IReadOnlyList<OperatorInfo> StringOperators { get; } =
    [
        new("$+", "str.concat", OperatorCategory.String, new(0, 0, 2, 1)),
        new("$*", "str.repeat", OperatorCategory.String, new(1, 0, 1, 1)),
        new("$d", "str.dup", OperatorCategory.Stack, new(0, 0, 1, 2)),
        new("$length", "str.length", OperatorCategory.String, new(0, 1, 1, 1)),
        new("$cutBegin", "str.cutBegin", OperatorCategory.String, new(1, 0, 1, 1)),
        new("$cutEnd", "str.cutEnd", OperatorCategory.String, new(1, 0, 1, 1)),
        new("$SetLengthR", "str.setLengthR", OperatorCategory.String, new(1, 1, 1, 1)),
        new("$SetLengthL", "str.setLengthL", OperatorCategory.String, new(1, 1, 1, 1)),
        new("$SetLengthC", "str.setLengthC", OperatorCategory.String, new(1, 1, 1, 1)),
        new("$RemoveSpaces", "str.removeSpaces", OperatorCategory.String, new(0, 0, 1, 1)),
        new("$=", "str.eq", OperatorCategory.StringComparison, new(0, 1, 2, 0)),
        new("$<", "str.lt", OperatorCategory.StringComparison, new(0, 1, 2, 0)),
        new("$>", "str.gt", OperatorCategory.StringComparison, new(0, 1, 2, 0)),
        new("$<=", "str.le", OperatorCategory.StringComparison, new(0, 1, 2, 0)),
        new("$>=", "str.ge", OperatorCategory.StringComparison, new(0, 1, 2, 0)),
        new("$IntToStr", "str.intToStr", OperatorCategory.Conversion, new(1, 0, 0, 1)),
        new("$IntToStrEnh", "str.intToStrEnh", OperatorCategory.Conversion, new(1, 0, 1, 1)),
        new("$StrToFloat", "str.strToFloat", OperatorCategory.Conversion, new(0, 1, 1, 0)),
        new("$msg", "str.msg", OperatorCategory.Debug, new(0, 0, 1, 1)),
    ];

    private static readonly FrozenDictionary<string, OperatorInfo> Floats = FloatOperators.ToFrozenDictionary(o => o.Token, StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, OperatorInfo> Strings = StringOperators.ToFrozenDictionary(o => o.Token, StringComparer.Ordinal);

    public static OperatorInfo? FindFloat(string token) => Floats.GetValueOrDefault(token);

    public static OperatorInfo? FindString(string token) => Strings.GetValueOrDefault(token);

    public static OperatorInfo? FindStringIgnoreCase(string token)
        => StringOperators.FirstOrDefault(o => string.Equals(o.Token, token, StringComparison.OrdinalIgnoreCase));

    public static OperatorInfo? FindFloatIgnoreCase(string token)
        => FloatOperators.FirstOrDefault(o => string.Equals(o.Token, token, StringComparison.OrdinalIgnoreCase));
}
