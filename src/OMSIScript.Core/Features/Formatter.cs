using OMSIScript.Syntax;
using OMSIScript.Text;

namespace OMSIScript.Features;

public sealed record FormattingOptions(bool InsertSpaces = false, int TabSize = 4, bool TrimTrailingWhitespace = true);

public static class Formatter
{
    public static IReadOnlyList<TextChange> Format(ScriptDocument document, FormattingOptions options, int? firstLine = null, int? lastLine = null)
    {
        var source = document.Source;
        var lineCount = source.LineCount;
        var continuation = new bool[lineCount];
        var openEnd = new bool[lineCount];

        foreach (var command in document.Commands)
        {
            var start = source.GetPosition(command.Span.Start).Line;
            var end = source.GetPosition(command.Span.End).Line;
            if (end > start)
            {
                openEnd[start] = true;
                for (var line = start + 1; line <= end; line++)
                    continuation[line] = true;
            }
        }

        if (document.Lex.UnterminatedString is { } open)
        {
            var line = source.GetPosition(open.Start).Line;
            openEnd[line] = true;
            for (var l = line + 1; l < lineCount; l++)
                continuation[l] = true;
        }

        var unit = options.InsertSpaces ? new string(' ', Math.Max(1, options.TabSize)) : "\t";
        var changes = new List<TextChange>();
        var inBlock = false;
        var depth = 0;
        var commandIndex = 0;

        for (var line = 0; line < lineCount; line++)
        {
            var span = source.GetLineSpan(line);
            var text = source.Text.AsSpan(span.Start, span.Length);
            var inRange = (firstLine is null || line >= firstLine) && (lastLine is null || line <= lastLine);
            var isComment = text.Length > 0 && text[0] == '\'';

            var indentLevel = inBlock ? 1 + depth : depth;
            var lineCommands = new List<Command>();
            while (commandIndex < document.Commands.Count && source.GetPosition(document.Commands[commandIndex].Span.Start).Line == line)
                lineCommands.Add(document.Commands[commandIndex++]);

            if (lineCommands.Count > 0)
            {
                var first = lineCommands[0].Kind;
                if (lineCommands[0].IsBlockHeader || first == CommandKind.End)
                    indentLevel = 0;
                else if (first is CommandKind.Else or CommandKind.EndIf)
                    indentLevel = Math.Max(inBlock ? 1 : 0, indentLevel - 1);
            }

            foreach (var command in lineCommands)
            {
                if (command.IsBlockHeader)
                {
                    inBlock = true;
                    depth = 0;
                }
                else if (command.Kind == CommandKind.End)
                {
                    inBlock = false;
                    depth = 0;
                }
                else if (command.Kind == CommandKind.If)
                {
                    depth++;
                }
                else if (command.Kind == CommandKind.EndIf)
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            if (!inRange || isComment || continuation[line])
                continue;

            var content = text.TrimStart(" \t");
            if (!openEnd[line] && options.TrimTrailingWhitespace)
                content = content.TrimEnd(" \t");

            var formatted = content.Length == 0 ? string.Empty : string.Concat(string.Concat(Enumerable.Repeat(unit, indentLevel)), content);
            if (!text.SequenceEqual(formatted))
                changes.Add(new TextChange(document.Path, span, formatted));
        }

        return changes;
    }
}
