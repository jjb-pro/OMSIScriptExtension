using System.Text;

namespace OMSIScript.Text;

public static class TextFiles
{
    public static Encoding Windows1252 { get; } = CreateWindows1252();

    public static string Decode(ReadOnlySpan<byte> bytes, bool detectUtf8 = false)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);

        if (detectUtf8 && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        return Windows1252.GetString(bytes);
    }

    public static string? TryRead(string path, bool detectUtf8 = false)
    {
        try
        {
            return File.Exists(path) ? Decode(File.ReadAllBytes(path), detectUtf8) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n')
                continue;

            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines;
    }

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
