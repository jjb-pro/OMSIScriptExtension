using OMSIScript.Project;
using OMSIScript.Text;

namespace OMSIScript.Analysis;

public static class ProjectDiagnostics
{
    public static IReadOnlyList<ScriptDiagnostic> Compute(Workspace workspace, ScriptContext context)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        if (context.Owner is not { } owner)
            return diagnostics;

        var sources = new Dictionary<string, SourceText?>(PathComparer.Instance);
        SourceText? Source(string path)
        {
            if (!sources.TryGetValue(path, out var source))
                sources[path] = source = workspace.ReadText(path, detectUtf8: true) is { } text ? new SourceText(text) : null;
            return source;
        }

        void Report(string path, int line, DiagnosticDescriptor descriptor, params object[] args)
        {
            if (Source(path) is not { } source || line < 0 || line >= source.LineCount)
                return;
            diagnostics.Add(new ScriptDiagnostic(path, source.GetLineSpan(line), descriptor.DefaultSeverity, descriptor, descriptor.Format(args)));
        }

        string Location(string path, int line) => $"{Path.GetFileName(path)}:{line + 1}";

        foreach (var reference in owner.References)
        {
            if (!workspace.Exists(reference.FullPath))
                Report(owner.Path, reference.Line, DiagnosticCodes.MissingFile, reference.RawPath);
        }

        foreach (var problem in owner.Problems)
            Report(owner.Path, problem.Line, DiagnosticCodes.ObjectFileError, problem.Message);

        CheckVariables(context.FloatVariables);
        CheckVariables(context.StringVariables);

        foreach (var list in context.VarLists.Concat(context.StringVarLists).Where(l => l.Exists))
        {
            foreach (var entry in list.Entries)
            {
                if (entry.Name.Length > 0 && entry.Name.Trim().Length != entry.Name.Length)
                    Report(list.Path, entry.Line, DiagnosticCodes.VarListWhitespace, entry.Name);
            }
        }

        var constants = new Dictionary<string, ConstantDefinition>(AsciiIgnoreCaseComparer.Instance);
        var curves = new Dictionary<string, CurveDefinition>(AsciiIgnoreCaseComparer.Instance);
        var aborted = false;
        foreach (var file in context.ConstFiles.Where(f => f.Exists))
        {
            if (aborted)
                break;

            foreach (var problem in file.Problems)
            {
                switch (problem.Kind)
                {
                    case ConstFileProblemKind.InvalidNumber:
                        Report(file.Path, problem.Line, DiagnosticCodes.ConstFileError, $"'{problem.Text}' is not a valid number.");
                        break;
                    case ConstFileProblemKind.UnexpectedEnd:
                        Report(file.Path, problem.Line, DiagnosticCodes.ConstFileError, "Unexpected end of file. A value is missing.");
                        break;
                    case ConstFileProblemKind.PointBeforeCurve:
                        Report(file.Path, problem.Line, DiagnosticCodes.ConstFileError, "'[pnt]' must follow a '[newcurve]'.");
                        break;
                    case ConstFileProblemKind.CurveWithoutPoints:
                        Report(file.Path, problem.Line, DiagnosticCodes.ConstFileError, $"The curve '{problem.Text}' has no points.");
                        break;
                    case ConstFileProblemKind.DescendingPoints:
                        Report(file.Path, problem.Line, DiagnosticCodes.ConstFileWarning, "Curve points must be sorted by ascending x values.");
                        break;
                }
            }

            foreach (var constant in file.Constants)
            {
                if (!constants.TryAdd(constant.Name, constant))
                    Report(file.Path, constant.Line, DiagnosticCodes.DuplicateConstant, "constant", constant.Name, Location(constants[constant.Name].Path, constants[constant.Name].Line));
            }

            foreach (var curve in file.Curves)
            {
                if (!curves.TryAdd(curve.Name, curve))
                    Report(file.Path, curve.Line, DiagnosticCodes.DuplicateConstant, "curve", curve.Name, Location(curves[curve.Name].Path, curves[curve.Name].Line));
            }

            aborted = file.Aborted;
        }

        return diagnostics;

        void CheckVariables(SymbolTable<VariableDefinition> table)
        {
            var seen = new Dictionary<string, VariableDefinition>(AsciiIgnoreCaseComparer.Instance);
            foreach (var variable in table.Items)
            {
                if (variable.Name.Length == 0)
                    continue;

                if (!seen.TryAdd(variable.Name, variable) && variable.Origin == VariableOrigin.VarList)
                {
                    var first = seen[variable.Name];
                    var where = first.Path.Length > 0 ? Location(first.Path, first.Line) : "the predefined variable list";
                    Report(variable.Path, variable.Line, DiagnosticCodes.DuplicateVariable, variable.Name, where);
                }
            }
        }
    }
}
