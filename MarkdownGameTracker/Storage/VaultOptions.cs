namespace MarkdownGameTracker.Storage;

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public string Path { get; set; } = string.Empty;

    public string GamesDirectory { get; set; } = "Games";
}
