namespace OMSIScript.Language;

public enum ObjectKind
{
    Unknown,
    Vehicle,
    SceneryObject,
    Human,
}

public static class ObjectKinds
{
    public static ObjectKind FromObjectFile(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".bus" or ".ovh" => ObjectKind.Vehicle,
        ".sco" => ObjectKind.SceneryObject,
        ".hum" => ObjectKind.Human,
        _ => ObjectKind.Unknown,
    };

    public static bool IsObjectFile(string path) => FromObjectFile(path) != ObjectKind.Unknown;

    public static string DisplayName(ObjectKind kind) => kind switch
    {
        ObjectKind.Vehicle => "vehicle",
        ObjectKind.SceneryObject => "scenery object",
        ObjectKind.Human => "human",
        _ => "unknown object",
    };
}
