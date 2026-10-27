using System.Globalization;
using OMSIScript.Language;
using OMSIScript.Text;

namespace OMSIScript.Syntax;

public static class CommandClassifier
{
    public static IReadOnlyList<Command> Classify(LexResult lex)
    {
        var commands = new Command[lex.Tokens.Count];
        for (var i = 0; i < commands.Length; i++)
            commands[i] = Classify(lex.Tokens[i]);

        return commands;
    }

    public static Command Classify(RawToken token)
    {
        var text = token.Text;
        var span = token.Span;

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return Make(CommandKind.StringLiteral, token, text[1..^1], TextSpan.FromBounds(span.Start + 1, span.End - 1));

        if (text[0] == '$')
        {
            var op = OperatorCatalog.FindString(text);
            return op is null
                ? Make(CommandKind.IgnoredStringOperator, token)
                : new Command { Kind = CommandKind.StringOperator, Text = text, Span = span, Operator = op };
        }

        switch (text)
        {
            case "{endif}": return Make(CommandKind.EndIf, token);
            case "{else}": return Make(CommandKind.Else, token);
            case "{if}": return Make(CommandKind.If, token);
            case "{init}": return Make(CommandKind.Init, token);
            case "{frame}": return Make(CommandKind.Frame, token);
            case "{frame_ai}": return Make(CommandKind.FrameAI, token);
            case "{end}": return Make(CommandKind.End, token);
        }

        if (text.EndsWith('}'))
        {
            if (text.StartsWith("{macro:", StringComparison.Ordinal))
                return Make(CommandKind.MacroHeader, token, text[7..^1], TextSpan.FromBounds(span.Start + 7, span.End - 1));
            if (text.StartsWith("{trigger:", StringComparison.Ordinal))
                return Make(CommandKind.TriggerHeader, token, text[9..^1], TextSpan.FromBounds(span.Start + 9, span.End - 1));
        }

        if (text.Length == 2 && text[0] is 's' or 'l')
        {
            var kind = text[0] == 's' ? CommandKind.StoreRegister : CommandKind.LoadRegister;
            return char.IsAsciiDigit(text[1])
                ? new Command { Kind = kind, Text = text, Span = span, Register = text[1] - '0' }
                : Make(CommandKind.InvalidRegister, token);
        }

        if (OperatorCatalog.FindFloat(text) is { } floatOp)
            return new Command { Kind = CommandKind.Operator, Text = text, Span = span, Operator = floatOp };

        if (text.Length >= 6 && text[0] == '(' && text[^1] == ')' && text[2] == '.' && text[4] == '.')
            return ClassifyAccess(token);

        return TryParseNumber(text, out var value)
            ? new Command { Kind = CommandKind.Number, Text = text, Span = span, Value = value }
            : Make(CommandKind.Invalid, token);
    }

    public static bool TryParseNumber(string text, out float value)
    {
        value = 0;
        var i = 0;
        if (i < text.Length && text[i] is '+' or '-')
            i++;

        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
            return false;

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            if (i < text.Length && text[i] is '+' or '-')
                i++;

            var exponentDigits = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
                return false;
        }

        return i == text.Length && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Command ClassifyAccess(RawToken token)
    {
        var text = token.Text;
        var name = text[5..^1];
        var nameSpan = TextSpan.FromBounds(token.Span.Start + 5, token.Span.End - 1);
        var kind = (text[1], text[3]) switch
        {
            ('L', 'L') => CommandKind.LoadFloatVariable,
            ('S', 'L') => CommandKind.StoreFloatVariable,
            ('L', '$') => CommandKind.LoadStringVariable,
            ('S', '$') => CommandKind.StoreStringVariable,
            ('L', _) => CommandKind.LoadSystemVariable,
            ('S', _) => CommandKind.StoreSystemVariable,
            ('M', 'L') => CommandKind.MacroCall,
            ('M', 'V') => CommandKind.SystemMacroCall,
            ('C', _) => CommandKind.Constant,
            ('T', 'F') => CommandKind.SoundTriggerFile,
            ('T', _) => CommandKind.SoundTrigger,
            ('F', _) => CommandKind.Curve,
            _ => CommandKind.IgnoredAccess,
        };

        return Make(kind, token, name, nameSpan);
    }

    private static Command Make(CommandKind kind, RawToken token, string name = "", TextSpan nameSpan = default)
        => new() { Kind = kind, Text = token.Text, Span = token.Span, Name = name, NameSpan = nameSpan };
}
