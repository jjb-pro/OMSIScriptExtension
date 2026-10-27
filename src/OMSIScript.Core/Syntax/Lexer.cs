using System.Text;
using OMSIScript.Text;

namespace OMSIScript.Syntax;

public readonly record struct RawToken(string Text, TextSpan Span);

public sealed class LexResult(SourceText source, IReadOnlyList<RawToken> tokens, IReadOnlyList<int> commentLines, TextSpan? unterminatedString)
{
    public SourceText Source { get; } = source;

    public IReadOnlyList<RawToken> Tokens { get; } = tokens;

    public IReadOnlyList<int> CommentLines { get; } = commentLines;

    public TextSpan? UnterminatedString { get; } = unterminatedString;
}

public static class Lexer
{
    public static LexResult Tokenize(SourceText source)
    {
        var tokens = new List<RawToken>();
        var comments = new List<int>();
        var builder = new StringBuilder();
        var tokenStart = -1;
        var tokenEnd = -1;
        var inQuote = false;
        var quoteStart = -1;

        void Flush()
        {
            if (tokenStart < 0)
                return;

            tokens.Add(new RawToken(builder.ToString(), TextSpan.FromBounds(tokenStart, tokenEnd)));
            builder.Clear();
            tokenStart = -1;
        }

        var text = source.Text;
        for (var line = 0; line < source.LineCount; line++)
        {
            var span = source.GetLineSpan(line);
            if (span.Length == 0)
                continue;

            if (text[span.Start] == '\'')
            {
                comments.Add(line);
                continue;
            }

            for (var i = span.Start; i < span.End; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    if (inQuote)
                        quoteStart = i;
                }

                if ((c == ' ' || c == '\t') && !inQuote)
                {
                    Flush();
                    continue;
                }

                if (tokenStart < 0)
                    tokenStart = i;
                builder.Append(c);
                tokenEnd = i + 1;
            }

            if (inQuote)
                builder.Append(' ');
            else
                Flush();
        }

        if (inQuote && builder.Length > 0)
            builder.Length--;
        Flush();

        return new LexResult(source, tokens, comments, inQuote ? TextSpan.FromBounds(quoteStart, text.Length) : null);
    }
}
