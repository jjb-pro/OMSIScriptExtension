using System.Globalization;
using OMSIScript.Text;

namespace OMSIScript.Project;

public sealed record ConstantDefinition(string Name, float Value, string Path, int Line);

public sealed record CurvePoint(float X, float Y, int Line);

public sealed record CurveDefinition(string Name, IReadOnlyList<CurvePoint> Points, string Path, int Line)
{
    public float Evaluate(float x)
    {
        if (Points.Count == 0)
            return float.NaN;
        if (x < Points[0].X)
            return Points[0].Y;
        if (x >= Points[^1].X)
            return Points[^1].Y;

        var i = 1;
        while (i < Points.Count - 1 && Points[i].X <= x)
            i++;

        var a = Points[i - 1];
        var b = Points[i];
        return b.X == a.X ? a.Y : a.Y + (x - a.X) * (b.Y - a.Y) / (b.X - a.X);
    }
}

public enum ConstFileProblemKind
{
    InvalidNumber,
    PointBeforeCurve,
    UnexpectedEnd,
    CurveWithoutPoints,
    DescendingPoints,
    DuplicateConstant,
    DuplicateCurve,
}

public sealed record ConstFileProblem(ConstFileProblemKind Kind, int Line, string Text);

public sealed class ConstFile
{
    private ConstFile(string path, bool exists, IReadOnlyList<ConstantDefinition> constants, IReadOnlyList<CurveDefinition> curves,
        IReadOnlyList<ConstFileProblem> problems, bool aborted)
    {
        Path = path;
        Exists = exists;
        Constants = constants;
        Curves = curves;
        Problems = problems;
        Aborted = aborted;
    }

    public string Path { get; }

    public bool Exists { get; }

    public IReadOnlyList<ConstantDefinition> Constants { get; }

    public IReadOnlyList<CurveDefinition> Curves { get; }

    public IReadOnlyList<ConstFileProblem> Problems { get; }

    public bool Aborted { get; }

    public static ConstFile Parse(string path, string? text)
    {
        if (text is null)
            return new ConstFile(path, false, [], [], [], false);

        var lines = TextFiles.SplitLines(text);
        var constants = new List<ConstantDefinition>();
        var curves = new List<(string Name, int Line, List<CurvePoint> Points)>();
        var problems = new List<ConstFileProblem>();
        var aborted = false;

        bool TryNumber(int line, out float value)
        {
            value = 0;
            if (line >= lines.Count)
            {
                problems.Add(new ConstFileProblem(ConstFileProblemKind.UnexpectedEnd, lines.Count - 1, string.Empty));
                return false;
            }

            if (float.TryParse(lines[line].Trim(' ', '\t'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            problems.Add(new ConstFileProblem(ConstFileProblemKind.InvalidNumber, line, lines[line]));
            return false;
        }

        for (var i = 0; i < lines.Count && !aborted; i++)
        {
            switch (lines[i])
            {
                case "[const]":
                    {
                        var name = i + 1 < lines.Count ? lines[i + 1] : string.Empty;
                        if (!TryNumber(i + 2, out var value))
                        {
                            aborted = true;
                            break;
                        }

                        constants.Add(new ConstantDefinition(name, value, path, i + 1));
                        i += 2;
                        break;
                    }

                case "[newcurve]":
                    curves.Add((i + 1 < lines.Count ? lines[i + 1] : string.Empty, i + 1, []));
                    i++;
                    break;

                case "[pnt]":
                    {
                        if (curves.Count == 0)
                        {
                            problems.Add(new ConstFileProblem(ConstFileProblemKind.PointBeforeCurve, i, lines[i]));
                            break;
                        }

                        if (!TryNumber(i + 1, out var x) || !TryNumber(i + 2, out var y))
                        {
                            aborted = true;
                            break;
                        }

                        var points = curves[^1].Points;
                        if (points.Count > 0 && x < points[^1].X)
                            problems.Add(new ConstFileProblem(ConstFileProblemKind.DescendingPoints, i + 1, lines[i + 1]));
                        points.Add(new CurvePoint(x, y, i + 1));
                        i += 2;
                        break;
                    }
            }
        }

        var seenConstants = new HashSet<string>(AsciiIgnoreCaseComparer.Instance);
        foreach (var constant in constants)
        {
            if (!seenConstants.Add(constant.Name))
                problems.Add(new ConstFileProblem(ConstFileProblemKind.DuplicateConstant, constant.Line, constant.Name));
        }

        var seenCurves = new HashSet<string>(AsciiIgnoreCaseComparer.Instance);
        foreach (var curve in curves)
        {
            if (curve.Points.Count == 0)
                problems.Add(new ConstFileProblem(ConstFileProblemKind.CurveWithoutPoints, curve.Line, curve.Name));
            if (!seenCurves.Add(curve.Name))
                problems.Add(new ConstFileProblem(ConstFileProblemKind.DuplicateCurve, curve.Line, curve.Name));
        }

        return new ConstFile(path, true, constants,
            [.. curves.Select(c => new CurveDefinition(c.Name, c.Points, path, c.Line))], problems, aborted);
    }
}
