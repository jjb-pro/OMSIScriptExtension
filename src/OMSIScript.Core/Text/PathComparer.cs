namespace OMSIScript.Text;

public sealed class PathComparer : IEqualityComparer<string>
{
    public static PathComparer Instance { get; } = new();

    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public bool Equals(string? x, string? y) => string.Equals(Normalize(x), Normalize(y), Comparison);

    public int GetHashCode(string obj) => Normalize(obj)!.GetHashCode(Comparison);

    public static string? Normalize(string? path) => path?.Replace('\\', '/').TrimEnd('/');
}
