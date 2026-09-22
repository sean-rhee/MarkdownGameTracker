using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using static MarkdownGameTracker.Services.IgdbGameMapper;

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
    private const string ArtworkFields = "id,name,cover.image_id,artworks.image_id,artworks.width,artworks.height";
    private const string DescriptionFields =
        "id,name,summary,storyline,slug,url,first_release_date,"
        + "cover.image_id,artworks.image_id,artworks.width,artworks.height,"
        + "screenshots.image_id,screenshots.width,screenshots.height,"
        + "videos.name,videos.video_id";
    private static readonly TimeSpan MetadataCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly IgdbOptions _options = options.Value;
    private readonly IgdbGameMatching _matching = new(apiClient);

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
            ? $"igdb-description:title:v4:{normalizedTitle.ToLowerInvariant()}"
            : $"igdb-description:id:v4:{igdbGameId.Value}";
        if (cache.TryGetValue(cacheKey, out IgdbDescriptionResult? cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        var outcome = await LookupDescriptionAsync(normalizedTitle, igdbGameId, cancellationToken);
        var result = outcome.Games.Length > 0 || outcome.IsConfirmedMiss
            ? ToDescriptionResult(outcome.Games.FirstOrDefault())
            : IgdbDescriptionResult.Unavailable();
        cache.Set(
            cacheKey,
            result,
            outcome.IsComplete && IsStableStatus(result.Status) ? MetadataCacheDuration : FailureCacheDuration);
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
        var outcome = await _matching.FindAsync(normalizedTitle, matchFields, cancellationToken,
            includeAlternatives: true);
        var result = outcome.Games.Length > 0
            ? IgdbMatchSearchResult.Available(outcome.Games.Select(ToMatch).ToArray())
            : outcome.IsConfirmedMiss ? IgdbMatchSearchResult.NotFound() : IgdbMatchSearchResult.Unavailable();

        cache.Set(
            cacheKey,
            result,
            outcome.IsComplete && IsStableStatus(result.Status) ? MetadataCacheDuration : FailureCacheDuration);
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
                    + IgdbGameMatching.ExactQuery(game.Title, ArtworkFields) + " };"));
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
                var outcome = await _matching.FindAsync(requested.Title, ArtworkFields, cancellationToken,
                    exactResult: matches is null ? IgdbQueryResult.Failed() : IgdbQueryResult.Success(matches.Result));
                allQueriesSucceeded &= outcome.IsComplete;
                if (outcome.Games.Length > 0 || outcome.IsConfirmedMiss)
                {
                    CacheArtwork(requested, outcome.Games.FirstOrDefault(), artwork, outcome.IsComplete);
                }
            }
        }

        return !allQueriesSucceeded && artwork.Count == 0
            ? IgdbCardArtworkResult.Unavailable()
            : IgdbCardArtworkResult.Available(artwork);
    }

    private async Task<IgdbMatchOutcome> LookupDescriptionAsync(
        string title, long? igdbGameId, CancellationToken cancellationToken)
    {
        if (igdbGameId is null)
            return await _matching.FindAsync(title, DescriptionFields, cancellationToken);

        var result = await apiClient.QueryGamesAsync(title,
            $"fields {DescriptionFields}; where id = {igdbGameId.Value}; limit 1;", cancellationToken);
        return new(result.Games, result.Succeeded);
    }

    private static bool IsStableStatus(string status) =>
        status is IgdbDescriptionResult.AvailableStatus or IgdbDescriptionResult.NotFoundStatus;

    private static string GetArtworkCacheKey(IgdbArtworkLookup game) =>
        game.IgdbGameId is > 0
            ? $"igdb-card-artwork:id:{game.IgdbGameId.Value}"
            : $"igdb-card-artwork:title:{game.Title.Trim().ToLowerInvariant()}";

    private void CacheArtwork(
        IgdbArtworkLookup requested,
        IgdbGame? match,
        ICollection<IgdbCardArtwork> results, bool isComplete = true)
    {
        var selectedArtwork = match is null ? null : ToCardArtwork(requested.GameId, match);
        cache.Set(
            GetArtworkCacheKey(requested),
            new IgdbArtworkCacheEntry(selectedArtwork),
            isComplete ? MetadataCacheDuration : FailureCacheDuration);
        if (selectedArtwork is not null)
        {
            results.Add(selectedArtwork);
        }
    }

    private sealed record IgdbArtworkCacheEntry(IgdbCardArtwork? Artwork);
}
