namespace OMSIScript.Language;

public enum KeywordKind
{
    Block,
    NamedBlock,
    End,
    Condition,
}

public sealed record KeywordInfo(string Token, string Id, KeywordKind Kind)
{
    public string DocumentationKey => "kw." + Id;
}

public static class KeywordCatalog
{
    public static IReadOnlyList<KeywordInfo> Keywords { get; } =
    [
        new("{init}", "init", KeywordKind.Block),
        new("{frame}", "frame", KeywordKind.Block),
        new("{frame_ai}", "frame_ai", KeywordKind.Block),
        new("{end}", "end", KeywordKind.End),
        new("{if}", "if", KeywordKind.Condition),
        new("{else}", "else", KeywordKind.Condition),
        new("{endif}", "endif", KeywordKind.Condition),
    ];

    public static KeywordInfo Macro { get; } = new("{macro:", "macro", KeywordKind.NamedBlock);

    public static KeywordInfo Trigger { get; } = new("{trigger:", "trigger", KeywordKind.NamedBlock);

    public static KeywordInfo? Find(string token) => Keywords.FirstOrDefault(k => k.Token == token);
}
