namespace MarkdownGameTracker.Models;

public static class GameSearch
{
    // Use the same fields and invariant uppercase normalization in rendered cards,
    // API filtering, and JavaScript (single-character, locale-independent casing).
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static string GetText(GameNote game) => Normalize(
        $"{game.Title}\n{GameNoteMetadata.GetString(game, GameNoteMetadata.PlatformKey)}\n{game.Markdown}");

    public static bool Matches(GameNote game, string? search) =>
        string.IsNullOrWhiteSpace(search) || GetText(game).Contains(Normalize(search), StringComparison.Ordinal);
}
