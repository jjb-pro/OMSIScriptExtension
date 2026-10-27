namespace OMSIScript.Text;

public sealed class AsciiIgnoreCaseComparer : IEqualityComparer<string>, IComparer<string>
{
    public static AsciiIgnoreCaseComparer Instance { get; } = new();

    public bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null || x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (ToUpper(x[i]) != ToUpper(y[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(string obj)
    {
        var hash = new HashCode();
        foreach (var c in obj)
            hash.Add(ToUpper(c));
        return hash.ToHashCode();
    }

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return x is null ? (y is null ? 0 : -1) : 1;

        var length = Math.Min(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            var diff = ToUpper(x[i]) - ToUpper(y[i]);
            if (diff != 0)
                return diff;
        }

        return x.Length - y.Length;
    }

    private static char ToUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
}
