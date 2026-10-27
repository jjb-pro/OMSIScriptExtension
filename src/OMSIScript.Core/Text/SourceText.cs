namespace OMSIScript.Text;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public bool ContainsOrTouches(int offset) => offset >= Start && offset <= End;
}

public readonly record struct LinePosition(int Line, int Character) : IComparable<LinePosition>
{
    public int CompareTo(LinePosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Character.CompareTo(other.Character);
}

public readonly record struct LineRange(LinePosition Start, LinePosition End);

public sealed class SourceText
{
    private readonly int[] _lineStarts;
    private readonly int[] _lineEnds;

    public SourceText(string text)
    {
        Text = text;

        var starts = new List<int> { 0 };
        var ends = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n')
                continue;

            ends.Add(i);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            starts.Add(i + 1);
        }

        ends.Add(text.Length);
        _lineStarts = [.. starts];
        _lineEnds = [.. ends];
    }

    public string Text { get; }

    public int Length => Text.Length;

    public int LineCount => _lineStarts.Length;

    public TextSpan GetLineSpan(int line) => TextSpan.FromBounds(_lineStarts[line], _lineEnds[line]);

    public int GetLineIndex(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        var index = Array.BinarySearch(_lineStarts, offset);
        return index >= 0 ? index : ~index - 1;
    }

    public LinePosition GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        var line = GetLineIndex(offset);
        return new LinePosition(line, Math.Min(offset, _lineEnds[line]) - _lineStarts[line]);
    }

    public LineRange GetRange(TextSpan span) => new(GetPosition(span.Start), GetPosition(span.End));

    public int GetOffset(LinePosition position)
    {
        if (position.Line < 0)
            return 0;
        if (position.Line >= _lineStarts.Length)
            return Text.Length;

        return Math.Min(_lineStarts[position.Line] + Math.Max(0, position.Character), _lineEnds[position.Line]);
    }

    public TextSpan GetSpan(LineRange range) => TextSpan.FromBounds(GetOffset(range.Start), GetOffset(range.End));

    public SourceText WithChange(LineRange range, string newText)
    {
        var span = GetSpan(range);
        return new SourceText(string.Concat(Text.AsSpan(0, span.Start), newText, Text.AsSpan(span.End)));
    }
}
