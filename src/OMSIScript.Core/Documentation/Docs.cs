using System.Globalization;
using System.Resources;
using System.Text;
using OMSIScript.Language;

namespace OMSIScript.Documentation;

public static class Docs
{
    private static readonly ResourceManager Resources = new("OMSIScript.Resources.Documentation", typeof(Docs).Assembly) { IgnoreCase = true };

    public static string? Get(string key) => Resources.GetString(key, CultureInfo.InvariantCulture);

    public static string Format(string key, params object[] args)
        => Get(key) is { } text ? string.Format(CultureInfo.InvariantCulture, text, args) : string.Empty;

    public static string? Variable(ObjectKind kind, string name, bool isString)
    {
        if (isString)
            return kind is ObjectKind.Vehicle or ObjectKind.Unknown ? Pattern("var.string.", name) : null;

        return kind switch
        {
            ObjectKind.Vehicle => Pattern("var.vehicle.", name),
            ObjectKind.SceneryObject => Pattern("var.scenery.", name),
            ObjectKind.Human => Pattern("var.human.", name),
            _ => Pattern("var.vehicle.", name) ?? Pattern("var.scenery.", name) ?? Pattern("var.human.", name),
        };
    }

    public static string? Trigger(string name) => Pattern("trigger.", name);

    private static string? Pattern(string prefix, string name)
        => Get(prefix + name) ?? (name.Any(char.IsAsciiDigit) ? Get(prefix + WithPlaceholders(name)) : null);

    private static string WithPlaceholders(string name)
    {
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsAsciiDigit(name[i]))
                builder.Append(name[i]);
            else if (i == 0 || !char.IsAsciiDigit(name[i - 1]))
                builder.Append('#');
        }

        return builder.ToString();
    }

    public static string StackEffect(StackEffect effect)
    {
        var parts = new List<string>();
        Describe(parts, "float", effect.FloatsRead, effect.FloatsLeft);
        Describe(parts, "string", effect.StringsRead, effect.StringsLeft);
        return parts.Count == 0 ? "Stacks unchanged" : string.Join(" · ", parts);
    }

    private static void Describe(List<string> parts, string stack, int read, int left)
    {
        if (read == 0 && left == 0)
            return;

        var net = left - read;
        var change = net switch
        {
            > 0 => $"+{net}",
            < 0 => $"−{-net}",
            _ => "±0",
        };
        parts.Add($"{stack} stack: uses {read}, leaves {left} ({change})");
    }
}
