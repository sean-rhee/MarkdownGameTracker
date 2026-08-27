using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
}

public sealed class IgdbDescriptionService(
    HttpClient httpClient,
    IOptions<IgdbOptions> options,
    IMemoryCache cache,
    ILogger<IgdbDescriptionService> logger) : IIgdbDescriptionService
{
    private const string DescriptionFields =
        "id,name,summary,storyline,slug,url,first_release_date,"
        + "cover.image_id,artworks.image_id,artworks.width,artworks.height,"
        + "screenshots.image_id,screenshots.width,screenshots.height";
    private static readonly TimeSpan MetadataCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly IgdbOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private AccessToken? _accessToken;

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
            ? $"igdb-description:title:{normalizedTitle.ToLowerInvariant()}"
            : $"igdb-description:id:{igdbGameId.Value}";
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
        var cacheKey = $"igdb-matches:{normalizedTitle.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out IgdbMatchSearchResult? cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        var query = $"search \"{EscapeSearchText(normalizedTitle)}\"; "
                    + "fields id,name,first_release_date,slug,url,cover.image_id; limit 10;";
        var queryResult = await QueryGamesAsync(normalizedTitle, query, cancellationToken);
        var result = !queryResult.Succeeded
            ? IgdbMatchSearchResult.Unavailable()
            : queryResult.Games.Length == 0
                ? IgdbMatchSearchResult.NotFound()
                : IgdbMatchSearchResult.Available(queryResult.Games.Select(ToMatch).ToArray());

        cache.Set(
            cacheKey,
            result,
            IsStableStatus(result.Status) ? MetadataCacheDuration : FailureCacheDuration);
        return result;
    }

    private async Task<IgdbDescriptionResult> LookupDescriptionAsync(
        string title,
        long? igdbGameId,
        CancellationToken cancellationToken)
    {
        var query = igdbGameId is null
            ? $"search \"{EscapeSearchText(title)}\"; "
              + $"fields {DescriptionFields}; limit 10;"
            : $"fields {DescriptionFields}; "
              + $"where id = {igdbGameId.Value}; limit 1;";
        var queryResult = await QueryGamesAsync(title, query, cancellationToken);
        if (!queryResult.Succeeded)
        {
            return IgdbDescriptionResult.Unavailable();
        }

        var match = igdbGameId is null
            ? queryResult.Games.FirstOrDefault(game =>
                  string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase))
              ?? queryResult.Games.FirstOrDefault()
            : queryResult.Games.FirstOrDefault();
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
        if (string.IsNullOrWhiteSpace(description)
            && coverUrl is null
            && heroUrl is null
            && screenshots.Length == 0)
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
            screenshots);
    }

    private async Task<IgdbQueryResult> QueryGamesAsync(
        string title,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendGamesQueryAsync(query, refreshToken: false, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _accessToken = null;
                response.Dispose();
                response = await SendGamesQueryAsync(query, refreshToken: true, cancellationToken);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "IGDB game lookup failed with status code {StatusCode}.",
                        response.StatusCode);
                    return IgdbQueryResult.Failed();
                }

                var games = await response.Content.ReadFromJsonAsync<IgdbGame[]>(cancellationToken)
                    ?? [];
                return IgdbQueryResult.Success(games);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("IGDB game lookup timed out for {GameTitle}.", title);
            return IgdbQueryResult.Failed();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "IGDB game lookup failed for {GameTitle}.", title);
            return IgdbQueryResult.Failed();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IGDB returned an invalid response for {GameTitle}.", title);
            return IgdbQueryResult.Failed();
        }
    }

    private async Task<HttpResponseMessage> SendGamesQueryAsync(
        string query,
        bool refreshToken,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(refreshToken, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games")
        {
            Content = new StringContent(query, Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("Client-ID", _options.ClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && _accessToken is { } currentToken && currentToken.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return currentToken.Value;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessToken is { } lockedToken && lockedToken.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return lockedToken.Value;
            }

            using var tokenRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "https://id.twitch.tv/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                    ["grant_type"] = "client_credentials"
                })
            };
            using var response = await httpClient.SendAsync(tokenRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            var tokenResponse = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(cancellationToken)
                ?? throw new JsonException("Twitch returned an empty token response.");
            var lifetime = TimeSpan.FromSeconds(Math.Max(1, tokenResponse.ExpiresIn * 0.9));
            _accessToken = new AccessToken(tokenResponse.AccessToken, DateTimeOffset.UtcNow.Add(lifetime));
            return _accessToken.Value;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string EscapeSearchText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsStableStatus(string status) =>
        status is IgdbDescriptionResult.AvailableStatus or IgdbDescriptionResult.NotFoundStatus;

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

    private sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

    private sealed record TwitchTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record IgdbGame(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("storyline")] string? Storyline,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("first_release_date")] long? FirstReleaseDate,
        [property: JsonPropertyName("cover")] IgdbImage? Cover,
        [property: JsonPropertyName("artworks")] IgdbImage[]? Artworks,
        [property: JsonPropertyName("screenshots")] IgdbImage[]? Screenshots);

    private sealed record IgdbImage(
        [property: JsonPropertyName("image_id")] string ImageId,
        [property: JsonPropertyName("width")] int? Width,
        [property: JsonPropertyName("height")] int? Height);

    private sealed record IgdbQueryResult(bool Succeeded, IgdbGame[] Games)
    {
        public static IgdbQueryResult Success(IgdbGame[] games) => new(true, games);

        public static IgdbQueryResult Failed() => new(false, []);
    }
}

public sealed record IgdbDescriptionResult(
    string Status,
    long? IgdbGameId,
    string? Description,
    string? MatchedTitle,
    string? SourceUrl,
    string? CoverUrl,
    string? HeroUrl,
    IReadOnlyList<IgdbScreenshot> Screenshots)
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
        IReadOnlyList<IgdbScreenshot>? screenshots = null) =>
        new(
            AvailableStatus,
            igdbGameId,
            description,
            matchedTitle,
            sourceUrl,
            coverUrl,
            heroUrl,
            screenshots ?? []);

    public static IgdbDescriptionResult NotConfigured() =>
        new(NotConfiguredStatus, null, null, null, null, null, null, []);

    public static IgdbDescriptionResult NotFound() =>
        new(NotFoundStatus, null, null, null, null, null, null, []);

    public static IgdbDescriptionResult Unavailable() =>
        new(UnavailableStatus, null, null, null, null, null, null, []);
}

public sealed record IgdbScreenshot(string ThumbnailUrl, string FullSizeUrl);

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
