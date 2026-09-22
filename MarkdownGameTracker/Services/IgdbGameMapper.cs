namespace MarkdownGameTracker.Services;

internal static class IgdbGameMapper
{
    public static IgdbDescriptionResult ToDescriptionResult(IgdbGame? match)
    {
        if (match is null)
        {
            return IgdbDescriptionResult.NotFound();
        }

        var description = !string.IsNullOrWhiteSpace(match.Summary)
            ? match.Summary
            : match.Storyline;
        var coverUrl = BuildImageUrl(match.Cover?.ImageId, "cover_big_2x");
        var heroImage = match.Artworks?
                            .Where(image => image.Width > image.Height)
                            .OrderByDescending(image => (long)image.Width.GetValueOrDefault()
                                                        * image.Height.GetValueOrDefault())
                            .FirstOrDefault()
                        ?? match.Screenshots?.FirstOrDefault()
                        ?? match.Artworks?.FirstOrDefault();
        var heroUrl = BuildImageUrl(heroImage?.ImageId, "1080p");
        var artworks = ToMediaImages(match.Artworks);
        var screenshots = (match.Screenshots ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image.ImageId))
            .DistinctBy(image => image.ImageId, StringComparer.Ordinal)
            .Select(ToMediaImage)
            .ToArray();
        var videos = (match.Videos ?? [])
            .Where(item => IsValidYouTubeVideoId(item.VideoId))
            .DistinctBy(item => item.VideoId, StringComparer.Ordinal)
            .Select(item => new IgdbVideo(
                string.IsNullOrWhiteSpace(item.Name) ? "Game video" : item.Name.Trim(),
                $"https://www.youtube-nocookie.com/embed/{item.VideoId}"))
            .ToArray();
        if (string.IsNullOrWhiteSpace(description)
            && coverUrl is null
            && heroUrl is null
            && artworks.Length == 0
            && screenshots.Length == 0
            && videos.Length == 0)
        {
            return IgdbDescriptionResult.NotFound();
        }

        return IgdbDescriptionResult.Available(
            match.Id,
            description?.Trim(),
            match.Name,
            GetSourceUrl(match),
            coverUrl,
            heroUrl,
            artworks,
            screenshots,
            videos);
    }

    private static IgdbScreenshot[] ToMediaImages(IgdbImage[]? images) =>
        (images ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image.ImageId))
            .DistinctBy(image => image.ImageId, StringComparer.Ordinal)
            .Select(ToMediaImage)
            .ToArray();

    private static IgdbScreenshot ToMediaImage(IgdbImage image) =>
        new(
            BuildImageUrl(image.ImageId, "screenshot_med_2x")!,
            BuildImageUrl(image.ImageId, "1080p")!);

    private static bool IsValidYouTubeVideoId(string? videoId) =>
        videoId is { Length: 11 }
        && videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static IgdbCardArtwork? ToCardArtwork(string gameId, IgdbGame game)
    {
        var landscape = game.Artworks?
            .Where(image => image.Width > image.Height)
            .OrderByDescending(image => (long)image.Width.GetValueOrDefault()
                                        * image.Height.GetValueOrDefault())
            .FirstOrDefault();
        var artworkUrl = BuildImageUrl(landscape?.ImageId, "720p");
        if (artworkUrl is not null)
        {
            return new IgdbCardArtwork(gameId, game.Id, game.Name, artworkUrl, "artwork");
        }

        var coverUrl = BuildImageUrl(game.Cover?.ImageId, "cover_big_2x");
        return coverUrl is null
            ? null
            : new IgdbCardArtwork(gameId, game.Id, game.Name, coverUrl, "cover");
    }

    public static IgdbGameMatch ToMatch(IgdbGame game) =>
        new(
            game.Id,
            game.Name,
            GetReleaseYear(game.FirstReleaseDate),
            GetSourceUrl(game),
            BuildImageUrl(game.Cover?.ImageId, "cover_small_2x"));

    private static string? BuildImageUrl(string? imageId, string size) =>
        string.IsNullOrWhiteSpace(imageId)
            ? null
            : $"https://images.igdb.com/igdb/image/upload/t_{size}/{Uri.EscapeDataString(imageId)}.jpg";

    private static int? GetReleaseYear(long? timestamp)
    {
        if (timestamp is null)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).Year;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string GetSourceUrl(IgdbGame game)
    {
        if (Uri.TryCreate(game.Url, UriKind.Absolute, out var sourceUri)
            && sourceUri.Scheme == Uri.UriSchemeHttps
            && (sourceUri.Host.Equals("igdb.com", StringComparison.OrdinalIgnoreCase)
                || sourceUri.Host.EndsWith(".igdb.com", StringComparison.OrdinalIgnoreCase)))
        {
            return sourceUri.ToString();
        }

        return !string.IsNullOrWhiteSpace(game.Slug)
            ? $"https://www.igdb.com/games/{Uri.EscapeDataString(game.Slug)}"
            : "https://www.igdb.com";
    }

}
