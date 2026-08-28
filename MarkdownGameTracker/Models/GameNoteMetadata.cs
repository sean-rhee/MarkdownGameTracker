using System.Globalization;

namespace MarkdownGameTracker.Models;

public static class GameNoteMetadata
{
    public const string TitleKey = "title";
    public const string PlatformKey = "platform";
    public const string StartDateKey = "start_date";
    public const string CompletionDateKey = "completion_date";

    public static string? GetString(GameNote game, string key) =>
        GetString(game.Frontmatter, key);

    public static string? GetString(
        IReadOnlyDictionary<string, object?> frontmatter,
        string key)
    {
        if (!frontmatter.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static DateOnly? GetDate(GameNote game, string key)
    {
        var value = GetString(game, key);
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }
}
