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
    Task<IgdbDescriptionResult> GetDescriptionAsync(string title, CancellationToken cancellationToken);
}

public sealed class IgdbDescriptionService(
    HttpClient httpClient,
    IOptions<IgdbOptions> options,
    IMemoryCache cache,
    ILogger<IgdbDescriptionService> logger) : IIgdbDescriptionService
{
    private static readonly TimeSpan DescriptionCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly IgdbOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private AccessToken? _accessToken;

    public async Task<IgdbDescriptionResult> GetDescriptionAsync(
        string title,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return IgdbDescriptionResult.NotConfigured();
        }

        var normalizedTitle = title.Trim();
        var cacheKey = $"igdb-description:{normalizedTitle.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out IgdbDescriptionResult? cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        var result = await LookupDescriptionAsync(normalizedTitle, cancellationToken);
        cache.Set(
            cacheKey,
            result,
            result.Status is IgdbDescriptionResult.AvailableStatus or IgdbDescriptionResult.NotFoundStatus
                ? DescriptionCacheDuration
                : FailureCacheDuration);
        return result;
    }

    private async Task<IgdbDescriptionResult> LookupDescriptionAsync(
        string title,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SearchGamesAsync(title, refreshToken: false, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _accessToken = null;
                response.Dispose();
                response = await SearchGamesAsync(title, refreshToken: true, cancellationToken);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "IGDB game lookup failed with status code {StatusCode}.",
                        response.StatusCode);
                    return IgdbDescriptionResult.Unavailable();
                }

                var games = await response.Content.ReadFromJsonAsync<IgdbGame[]>(cancellationToken)
                    ?? [];
                var match = games.FirstOrDefault(game =>
                                string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase))
                            ?? games.FirstOrDefault();

                if (match is null)
                {
                    return IgdbDescriptionResult.NotFound();
                }

                var description = !string.IsNullOrWhiteSpace(match.Summary)
                    ? match.Summary
                    : match.Storyline;
                if (string.IsNullOrWhiteSpace(description))
                {
                    return IgdbDescriptionResult.NotFound();
                }

                var sourceUrl = GetSourceUrl(match);

                return IgdbDescriptionResult.Available(
                    description.Trim(),
                    match.Name,
                    sourceUrl);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("IGDB game lookup timed out for {GameTitle}.", title);
            return IgdbDescriptionResult.Unavailable();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "IGDB game lookup failed for {GameTitle}.", title);
            return IgdbDescriptionResult.Unavailable();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IGDB returned an invalid response for {GameTitle}.", title);
            return IgdbDescriptionResult.Unavailable();
        }
    }

    private async Task<HttpResponseMessage> SearchGamesAsync(
        string title,
        bool refreshToken,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(refreshToken, cancellationToken);
        var escapedTitle = title
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var query = $"search \"{escapedTitle}\"; fields name,summary,storyline,slug,url; limit 10;";
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
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("storyline")] string? Storyline,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("url")] string? Url);
}

public sealed record IgdbDescriptionResult(
    string Status,
    string? Description,
    string? MatchedTitle,
    string? SourceUrl)
{
    public const string AvailableStatus = "available";
    public const string NotConfiguredStatus = "notConfigured";
    public const string NotFoundStatus = "notFound";
    public const string UnavailableStatus = "unavailable";

    public static IgdbDescriptionResult Available(string description, string matchedTitle, string sourceUrl) =>
        new(AvailableStatus, description, matchedTitle, sourceUrl);

    public static IgdbDescriptionResult NotConfigured() =>
        new(NotConfiguredStatus, null, null, null);

    public static IgdbDescriptionResult NotFound() =>
        new(NotFoundStatus, null, null, null);

    public static IgdbDescriptionResult Unavailable() =>
        new(UnavailableStatus, null, null, null);
}
