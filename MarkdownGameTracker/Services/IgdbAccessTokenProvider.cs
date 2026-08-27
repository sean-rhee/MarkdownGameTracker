using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MarkdownGameTracker.Services;

internal interface IIgdbAccessTokenProvider
{
    Task<IgdbAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken);

    Task<IgdbAccessToken> RefreshAccessTokenAsync(
        IgdbAccessToken rejectedToken,
        CancellationToken cancellationToken);
}

internal sealed record IgdbAccessToken(string Value, long Generation);

internal sealed class IgdbAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<IgdbOptions> options) : IIgdbAccessTokenProvider
{
    internal const string HttpClientName = "IgdbAuthentication";

    private readonly IgdbOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedAccessToken? _cachedToken;
    private long _generation;

    public async Task<IgdbAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var currentToken = Volatile.Read(ref _cachedToken);
        if (currentToken is not null && IsValid(currentToken))
        {
            return currentToken.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            currentToken = _cachedToken;
            return currentToken is not null && IsValid(currentToken)
                ? currentToken.Token
                : await RequestAccessTokenAsync(cancellationToken);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<IgdbAccessToken> RefreshAccessTokenAsync(
        IgdbAccessToken rejectedToken,
        CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            var currentToken = _cachedToken;
            if (currentToken is not null
                && IsValid(currentToken)
                && currentToken.Token.Generation != rejectedToken.Generation)
            {
                return currentToken.Token;
            }

            return await RequestAccessTokenAsync(cancellationToken);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<IgdbAccessToken> RequestAccessTokenAsync(CancellationToken cancellationToken)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "client_credentials"
            })
        };
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(tokenRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var tokenResponse = await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(cancellationToken)
            ?? throw new JsonException("Twitch returned an empty token response.");
        var lifetime = TimeSpan.FromSeconds(Math.Max(1, tokenResponse.ExpiresIn * 0.9));
        var token = new IgdbAccessToken(tokenResponse.AccessToken, ++_generation);
        Volatile.Write(
            ref _cachedToken,
            new CachedAccessToken(token, DateTimeOffset.UtcNow.Add(lifetime)));
        return token;
    }

    private static bool IsValid(CachedAccessToken token) =>
        token.ExpiresAtUtc > DateTimeOffset.UtcNow;

    private sealed record CachedAccessToken(IgdbAccessToken Token, DateTimeOffset ExpiresAtUtc);

    private sealed record TwitchTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
