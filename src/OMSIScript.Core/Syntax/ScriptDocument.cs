using OMSIScript.Text;

namespace OMSIScript.Syntax;

public sealed class ScriptDocument
{
    private ScriptDocument(string path, SourceText source)
    {
        Path = path;
        Source = source;
        Lex = Lexer.Tokenize(source);
        Commands = CommandClassifier.Classify(Lex);
    }

    public string Path { get; }

    public SourceText Source { get; }

    public LexResult Lex { get; }

    public IReadOnlyList<Command> Commands { get; }

    public static ScriptDocument Parse(string path, string text) => new(path, new SourceText(text));

    public static ScriptDocument Parse(string path, SourceText source) => new(path, source);

    public int FindCommandIndex(int offset)
    {
        int low = 0, high = Commands.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) >>> 1;
            var span = Commands[mid].Span;
            if (offset < span.Start)
                high = mid - 1;
            else if (offset > span.End)
                low = mid + 1;
            else
                return mid;
        }

        return -1;
    }

    public Command? FindCommand(int offset)
    {
        var index = FindCommandIndex(offset);
        return index < 0 ? null : Commands[index];
    }
}
