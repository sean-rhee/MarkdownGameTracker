using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownGameTracker.Storage;

public static class GameNoteFileNames
{
    private static readonly char[] InvalidTitleCharacters =
        Enumerable.Range(0, 32).Select(value => (char)value).Concat("<>:\"/\\|?*").ToArray();

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string CreateSafeId(string title, string suffix = "")
    {
        var displayTitle = ValidateDisplayTitle(title);
        var builder = new StringBuilder(displayTitle.Length + suffix.Length);
        foreach (var character in displayTitle)
        {
            if (InvalidTitleCharacters.Contains(character))
            {
                builder.Append(character == ':' ? " -" : " ");
            }
            else
            {
                builder.Append(character);
            }
        }

        var safeBase = Regex.Replace(builder.ToString(), @"\s+", " ").Trim(' ', '.');
        if (safeBase.Length == 0)
        {
            safeBase = "Game";
        }

        if (IsReservedName(safeBase))
        {
            safeBase = $"Game - {safeBase}";
        }

        var maximumBaseLength = 200 - suffix.Length;
        if (maximumBaseLength <= 0)
        {
            throw new ArgumentException("The note filename suffix is too long.", nameof(suffix));
        }

        if (safeBase.Length > maximumBaseLength)
        {
            safeBase = safeBase[..maximumBaseLength].TrimEnd(' ', '.');
        }

        return ValidateId($"{safeBase}{suffix}");
    }

    public static string ValidateDisplayTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (trimmed.Length > 200)
        {
            throw new ArgumentException("Title cannot be longer than 200 characters.", nameof(title));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Title contains unsupported control characters.", nameof(title));
        }

        if (trimmed is "." or ".."
            || trimmed.Contains("../", StringComparison.Ordinal)
            || trimmed.Contains("..\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Title contains an unsafe path sequence.", nameof(title));
        }

        return trimmed;
    }

    public static string ValidateId(string? id)
    {
        var trimmed = id?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Game-note ID is required.", nameof(id));
        }

        if (trimmed.Length > 200)
        {
            throw new ArgumentException("Game-note ID cannot be longer than 200 characters.", nameof(id));
        }

        if (trimmed.EndsWith('.') || trimmed.IndexOfAny(InvalidTitleCharacters) >= 0)
        {
            throw new ArgumentException("Game-note ID contains invalid filename characters.", nameof(id));
        }

        if (IsReservedName(trimmed))
        {
            throw new ArgumentException("Game-note ID is reserved by the operating system.", nameof(id));
        }

        return trimmed;
    }

    private static bool IsReservedName(string name) =>
        ReservedWindowsNames.Contains(name.Split('.')[0]);
}
