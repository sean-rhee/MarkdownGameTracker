using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MarkdownGameTracker.Services;

public interface IIgdbDescriptionService
{
    Task<IgdbDescriptionResult> GetDescriptionAsync(
        string title,
        long? igdbGameId,
        CancellationToken cancellationToken);

    Task<IgdbMatchSearchResult> SearchMatchesAsync(
        string title,
        CancellationToken cancellationToken);

    Task<IgdbCardArtworkResult> GetCardArtworkAsync(
        IReadOnlyList<IgdbArtworkLookup> games,
        CancellationToken cancellationToken);
}

internal sealed class IgdbDescriptionService(
    IIgdbApiClient apiClient,
    IOptions<IgdbOptions> options,
    IMemoryCache cache) : IIgdbDescriptionService
{
    private const string DescriptionFields =
        "id,name,summary,storyline,slug,url,first_release_date,"
        + "cover.image_id,artworks.image_id,artworks.width,artworks.height,"
        + "screenshots.image_id,screenshots.width,screenshots.height,"
        + "videos.name,videos.video_id";
    private static readonly TimeSpan MetadataCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly IgdbOptions _options = options.Value;

    public async Task<IgdbDescriptionResult> GetDescriptionAsync(
        string title,
        long? igdbGameId,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return IgdbDescriptionResult.NotConfigured();
        }

        var normalizedTitle = title.Trim();
        var cacheKey = igdbGameId is null
            ? $"igdb-description:title:v3:{normalizedTitle.ToLowerInvariant()}"
            : $"igdb-description:id:v3:{igdbGameId.Value}";
        if (cache.TryGetValue(cacheKey, out IgdbDescriptionResult? cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        var result = await LookupDescriptionAsync(normalizedTitle, igdbGameId, cancellationToken);
        cache.Set(
            cacheKey,
            result,
            IsStableStatus(result.Status) ? MetadataCacheDuration : FailureCacheDuration);
        return result;
    }

    public async Task<IgdbMatchSearchResult> SearchMatchesAsync(
        string title,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return IgdbMatchSearchResult.NotConfigured();
        }

        var normalizedTitle = title.Trim();
        var cacheKey = $"igdb-matches:v2:{normalizedTitle.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out IgdbMatchSearchResult? cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        const string matchFields = "id,name,first_release_date,slug,url,cover.image_id";
        var exactQuery = $"fields {matchFields}; "
                         + $"where name = \"{EscapeSearchText(normalizedTitle)}\"; limit 10;";
        var searchQuery = $"search \"{EscapeSearchText(normalizedTitle)}\"; "
                          + $"fields {matchFields}; where version_parent = null; limit 25;";
        var exactResult = await apiClient.QueryGamesAsync(normalizedTitle, exactQuery, cancellationToken);
        var searchResult = await apiClient.QueryGamesAsync(normalizedTitle, searchQuery, cancellationToken);
        var candidates = exactResult.Games
            .Where(game => string.Equals(game.Name, normalizedTitle, StringComparison.OrdinalIgnoreCase))
            .Concat(searchResult.Games)
            .DistinctBy(game => game.Id)
            .ToArray();
        var result = !exactResult.Succeeded && !searchResult.Succeeded
            ? IgdbMatchSearchResult.Unavailable()
            : candidates.Length == 0
                ? IgdbMatchSearchResult.NotFound()
                : IgdbMatchSearchResult.Available(candidates.Select(ToMatch).ToArray());

        cache.Set(
            cacheKey,
            result,
            IsStableStatus(result.Status) ? MetadataCacheDuration : FailureCacheDuration);
        return result;
    }

    public async Task<IgdbCardArtworkResult> GetCardArtworkAsync(
        IReadOnlyList<IgdbArtworkLookup> games,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return IgdbCardArtworkResult.NotConfigured();
        }

        var requestedGames = games
            .Where(game => !string.IsNullOrWhiteSpace(game.GameId)
                           && !string.IsNullOrWhiteSpace(game.Title))
            .DistinctBy(game => game.GameId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var artwork = new List<IgdbCardArtwork>();
        var pending = new List<IgdbArtworkLookup>();

        foreach (var game in requestedGames)
        {
            var cacheKey = GetArtworkCacheKey(game);
            if (cache.TryGetValue(cacheKey, out IgdbArtworkCacheEntry? cached)
                && cached is not null)
            {
                if (cached.Artwork is not null)
                {
                    artwork.Add(cached.Artwork with { GameId = game.GameId });
                }

                continue;
            }

            pending.Add(game);
        }

        var allQueriesSucceeded = true;
        var selectedGames = pending.Where(game => game.IgdbGameId is > 0).ToArray();
        if (selectedGames.Length > 0)
        {
            var ids = selectedGames.Select(game => game.IgdbGameId!.Value).Distinct().ToArray();
            var query = "fields id,name,cover.image_id,artworks.image_id,artworks.width,artworks.height; "
                        + $"where id = ({string.Join(',', ids)}); limit {ids.Length};";
            var selectedResult = await apiClient.QueryGamesAsync("card artwork by ID", query, cancellationToken);
            allQueriesSucceeded &= selectedResult.Succeeded;
            if (selectedResult.Succeeded)
            {
                var gamesById = selectedResult.Games.ToDictionary(game => game.Id);
                foreach (var requested in selectedGames)
                {
                    gamesById.TryGetValue(requested.IgdbGameId!.Value, out var match);
                    CacheArtwork(requested, match, artwork);
                }
            }
        }

        var titleGames = pending.Where(game => game.IgdbGameId is null or <= 0).ToArray();
        for (var offset = 0; offset < titleGames.Length; offset += 10)
        {
            var batch = titleGames.Skip(offset).Take(10).ToArray();
            var query = string.Join(
                Environment.NewLine,
                batch.Select((game, index) =>
                    $"query games \"game{index}\" {{ "
                    + "fields id,name,cover.image_id,artworks.image_id,artworks.width,artworks.height; "
                    + $"where name = \"{EscapeSearchText(game.Title.Trim())}\"; limit 10; }};"));
            var batchResult = await apiClient.QueryMultipleGamesAsync(query, cancellationToken);
            allQueriesSucceeded &= batchResult.Succeeded;
            if (!batchResult.Succeeded)
            {
                continue;
            }

            var resultsByName = batchResult.Results.ToDictionary(result => result.Name, StringComparer.Ordinal);
            for (var index = 0; index < batch.Length; index++)
            {
                var requested = batch[index];
                resultsByName.TryGetValue($"game{index}", out var matches);
                var match = matches?.Result.FirstOrDefault(game =>
                                string.Equals(game.Name, requested.Title.Trim(), StringComparison.OrdinalIgnoreCase))
                            ?? matches?.Result.FirstOrDefault();
                CacheArtwork(requested, match, artwork);
            }
        }

        return !allQueriesSucceeded && artwork.Count == 0
            ? IgdbCardArtworkResult.Unavailable()
            : IgdbCardArtworkResult.Available(artwork);
    }

    private async Task<IgdbDescriptionResult> LookupDescriptionAsync(
        string title,
        long? igdbGameId,
        CancellationToken cancellationToken)
    {
        if (igdbGameId is not null)
        {
            var selectedQuery = $"fields {DescriptionFields}; "
                                + $"where id = {igdbGameId.Value}; limit 1;";
            var selectedResult = await apiClient.QueryGamesAsync(title, selectedQuery, cancellationToken);
            return !selectedResult.Succeeded
                ? IgdbDescriptionResult.Unavailable()
                : ToDescriptionResult(selectedResult.Games.FirstOrDefault());
        }

        var exactQuery = $"fields {DescriptionFields}; "
                         + $"where name = \"{EscapeSearchText(title)}\"; limit 10;";
        var exactResult = await apiClient.QueryGamesAsync(title, exactQuery, cancellationToken);
        var match = exactResult.Games.FirstOrDefault(game =>
            string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return ToDescriptionResult(match);
        }

        var searchQuery = $"search \"{EscapeSearchText(title)}\"; "
                          + $"fields {DescriptionFields}; where version_parent = null; limit 25;";
        var searchResult = await apiClient.QueryGamesAsync(title, searchQuery, cancellationToken);
        if (!exactResult.Succeeded && !searchResult.Succeeded)
        {
            return IgdbDescriptionResult.Unavailable();
        }

        match = searchResult.Games.FirstOrDefault(game =>
                    string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase))
                ?? searchResult.Games.FirstOrDefault();
        return ToDescriptionResult(match);
    }

    private static IgdbDescriptionResult ToDescriptionResult(IgdbGame? match)
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
        var screenshots = (match.Screenshots ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image.ImageId))
            .DistinctBy(image => image.ImageId, StringComparer.Ordinal)
            .Take(6)
            .Select(image => new IgdbScreenshot(
                BuildImageUrl(image.ImageId, "screenshot_med_2x")!,
                BuildImageUrl(image.ImageId, "1080p")!))
            .ToArray();
        var video = (match.Videos ?? [])
            .Where(item => IsValidYouTubeVideoId(item.VideoId))
            .Select(item => new IgdbVideo(
                string.IsNullOrWhiteSpace(item.Name) ? "Game video" : item.Name.Trim(),
                $"https://www.youtube-nocookie.com/embed/{item.VideoId}"))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(description)
            && coverUrl is null
            && heroUrl is null
            && screenshots.Length == 0
            && video is null)
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
            screenshots,
            video);
    }

    private static bool IsValidYouTubeVideoId(string? videoId) =>
        videoId is { Length: 11 }
        && videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string EscapeSearchText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsStableStatus(string status) =>
        status is IgdbDescriptionResult.AvailableStatus or IgdbDescriptionResult.NotFoundStatus;

    private static string GetArtworkCacheKey(IgdbArtworkLookup game) =>
        game.IgdbGameId is > 0
            ? $"igdb-card-artwork:id:{game.IgdbGameId.Value}"
            : $"igdb-card-artwork:title:{game.Title.Trim().ToLowerInvariant()}";

    private void CacheArtwork(
        IgdbArtworkLookup requested,
        IgdbGame? match,
        ICollection<IgdbCardArtwork> results)
    {
        var selectedArtwork = match is null ? null : ToCardArtwork(requested.GameId, match);
        cache.Set(
            GetArtworkCacheKey(requested),
            new IgdbArtworkCacheEntry(selectedArtwork),
            MetadataCacheDuration);
        if (selectedArtwork is not null)
        {
            results.Add(selectedArtwork);
        }
    }

    private static IgdbCardArtwork? ToCardArtwork(string gameId, IgdbGame game)
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

    private static IgdbGameMatch ToMatch(IgdbGame game) =>
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

    private sealed record IgdbArtworkCacheEntry(IgdbCardArtwork? Artwork);
}
