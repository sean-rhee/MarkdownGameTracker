using System.Globalization;
using MarkdownGameTracker.Models;

namespace MarkdownGameTracker.Storage;

internal static class GameNoteUpdatePolicy
{
    // Top-level fields are authoritative; legacy frontmatter rating updates still
    // work, but pass through the same validation and explicit clear operation.
    public static Dictionary<string, object?> Apply(
        IReadOnlyDictionary<string, object?>? existing, string title, string? status,
        decimal? rating, bool clearRating, IReadOnlyDictionary<string, object?>? changes,
        GameNoteDocumentSerializer serializer)
    {
        ValidateRating(rating);
        var result = existing?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                     ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        decimal? frontmatterRating = null;
        var hasFrontmatterRating = false;
        foreach (var pair in changes ?? new Dictionary<string, object?>())
        {
            var value = serializer.NormalizeValue(pair.Value);
            if (pair.Key.Equals("rating", StringComparison.OrdinalIgnoreCase))
            {
                if (hasFrontmatterRating)
                {
                    throw new ArgumentException("Supply rating only once in frontmatter.", "rating");
                }
                hasFrontmatterRating = true;
                frontmatterRating = ParseRating(value);
                continue;
            }
            // These fields are controlled by the typed request, never raw metadata.
            if (pair.Key.Equals("status", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("title", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value is null)
            {
                result.Remove(pair.Key);
            }
            else
            {
                result[pair.Key] = value;
            }
        }

        if (clearRating && (rating is not null || frontmatterRating is not null))
        {
            throw new ArgumentException("Cannot set and clear a rating in the same update.", "rating");
        }

        if (clearRating || rating is not null || hasFrontmatterRating || existing is null)
        {
            foreach (var key in result.Keys.Where(key => key.Equals("rating", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                result.Remove(key);
            }
            var finalRating = clearRating ? null : rating ?? frontmatterRating;
            if (finalRating is not null)
            {
                result["rating"] = finalRating;
            }
        }

        result["type"] = "game";
        result[GameNoteMetadata.TitleKey] = title;
        result.TryAdd("hobby", new List<object?> { "[[Gaming]]" });
        if (status is not null)
        {
            result["status"] = GameStatuses.NormalizeRequired(status);
        }
        return result;
    }

    private static decimal? ParseRating(object? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is not (decimal or int or long or double or float or string)
            || !decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var rating))
        {
            throw new ArgumentException("Rating must be a number between 0 and 10.", "rating");
        }
        ValidateRating(rating);
        return rating;
    }

    public static void ValidateRating(decimal? rating)
    {
        if (rating is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 10.");
        }
    }
}
