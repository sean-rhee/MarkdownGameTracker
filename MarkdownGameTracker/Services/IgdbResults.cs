namespace MarkdownGameTracker.Services;

public sealed record IgdbDescriptionResult(
    string Status,
    long? IgdbGameId,
    string? Description,
    string? MatchedTitle,
    string? SourceUrl,
    string? CoverUrl,
    string? HeroUrl,
    IReadOnlyList<IgdbScreenshot> Artworks,
    IReadOnlyList<IgdbScreenshot> Screenshots,
    IReadOnlyList<IgdbVideo> Videos)
{
    public const string AvailableStatus = "available";
    public const string NotConfiguredStatus = "notConfigured";
    public const string NotFoundStatus = "notFound";
    public const string UnavailableStatus = "unavailable";

    public static IgdbDescriptionResult Available(
        long igdbGameId,
        string? description,
        string matchedTitle,
        string sourceUrl,
        string? coverUrl = null,
        string? heroUrl = null,
        IReadOnlyList<IgdbScreenshot>? artworks = null,
        IReadOnlyList<IgdbScreenshot>? screenshots = null,
        IReadOnlyList<IgdbVideo>? videos = null) =>
        new(
            AvailableStatus,
            igdbGameId,
            description,
            matchedTitle,
            sourceUrl,
            coverUrl,
            heroUrl,
            artworks ?? [],
            screenshots ?? [],
            videos ?? []);

    public static IgdbDescriptionResult NotConfigured() =>
        new(NotConfiguredStatus, null, null, null, null, null, null, [], [], []);

    public static IgdbDescriptionResult NotFound() =>
        new(NotFoundStatus, null, null, null, null, null, null, [], [], []);

    public static IgdbDescriptionResult Unavailable() =>
        new(UnavailableStatus, null, null, null, null, null, null, [], [], []);
}

public sealed record IgdbScreenshot(string ThumbnailUrl, string FullSizeUrl);

public sealed record IgdbVideo(string Name, string EmbedUrl);

public sealed record IgdbGameMatch(
    long IgdbGameId,
    string Title,
    int? ReleaseYear,
    string SourceUrl,
    string? CoverUrl = null);

public sealed record IgdbMatchSearchResult(string Status, IReadOnlyList<IgdbGameMatch> Matches)
{
    public static IgdbMatchSearchResult Available(IReadOnlyList<IgdbGameMatch> matches) =>
        new(IgdbDescriptionResult.AvailableStatus, matches);

    public static IgdbMatchSearchResult NotConfigured() =>
        new(IgdbDescriptionResult.NotConfiguredStatus, []);

    public static IgdbMatchSearchResult NotFound() =>
        new(IgdbDescriptionResult.NotFoundStatus, []);

    public static IgdbMatchSearchResult Unavailable() =>
        new(IgdbDescriptionResult.UnavailableStatus, []);
}

public sealed record IgdbArtworkLookup(string GameId, string Title, long? IgdbGameId);

public sealed record IgdbCardArtwork(
    string GameId,
    long IgdbGameId,
    string MatchedTitle,
    string ArtworkUrl,
    string Kind);

public sealed record IgdbCardArtworkResult(string Status, IReadOnlyList<IgdbCardArtwork> Artwork)
{
    public static IgdbCardArtworkResult Available(IReadOnlyList<IgdbCardArtwork> artwork) =>
        new(IgdbDescriptionResult.AvailableStatus, artwork);

    public static IgdbCardArtworkResult NotConfigured() =>
        new(IgdbDescriptionResult.NotConfiguredStatus, []);

    public static IgdbCardArtworkResult Unavailable() =>
        new(IgdbDescriptionResult.UnavailableStatus, []);
}
