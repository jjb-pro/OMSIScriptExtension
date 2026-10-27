namespace OMSIScript.Language;

public static class GameInstallation
{
    public const string Executable = "Omsi.exe";

    public static bool IsRoot(string directory) => File.Exists(Path.Combine(directory, Executable));

    public static string? FindRoot(string path)
    {
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        while (!string.IsNullOrEmpty(directory))
        {
            if (IsRoot(directory))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
