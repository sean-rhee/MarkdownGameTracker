using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MarkdownGameTracker.Services;

internal interface IIgdbApiClient
{
    Task<IgdbQueryResult> QueryGamesAsync(
        string operationLabel,
        string query,
        CancellationToken cancellationToken);

    Task<IgdbMultiQueryResult> QueryMultipleGamesAsync(
        string query,
        CancellationToken cancellationToken);
}

internal sealed class IgdbApiClient(
    HttpClient httpClient,
    IOptions<IgdbOptions> options,
    IIgdbAccessTokenProvider accessTokenProvider,
    ILogger<IgdbApiClient> logger) : IIgdbApiClient
{
    private readonly IgdbOptions _options = options.Value;

    public async Task<IgdbQueryResult> QueryGamesAsync(
        string operationLabel,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthenticatedQueryAsync("games", query, cancellationToken);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("IGDB game lookup failed with status code {StatusCode}.", response.StatusCode);
                    return IgdbQueryResult.Failed();
                }

                var games = await response.Content.ReadFromJsonAsync<IgdbGame[]>(cancellationToken) ?? [];
                return IgdbQueryResult.Success(games);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("IGDB game lookup timed out for {OperationLabel}.", operationLabel);
            return IgdbQueryResult.Failed();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "IGDB game lookup failed for {OperationLabel}.", operationLabel);
            return IgdbQueryResult.Failed();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IGDB returned an invalid response for {OperationLabel}.", operationLabel);
            return IgdbQueryResult.Failed();
        }
    }

    public async Task<IgdbMultiQueryResult> QueryMultipleGamesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthenticatedQueryAsync("multiquery", query, cancellationToken);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("IGDB multi-query failed with status code {StatusCode}.", response.StatusCode);
                    return IgdbMultiQueryResult.Failed();
                }

                var results = await response.Content.ReadFromJsonAsync<IgdbNamedGameResult[]>(cancellationToken) ?? [];
                return IgdbMultiQueryResult.Success(results);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("IGDB card artwork lookup timed out.");
            return IgdbMultiQueryResult.Failed();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "IGDB card artwork lookup failed.");
            return IgdbMultiQueryResult.Failed();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IGDB returned an invalid card artwork response.");
            return IgdbMultiQueryResult.Failed();
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedQueryAsync(
        string endpoint,
        string query,
        CancellationToken cancellationToken)
    {
        var token = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var response = await SendQueryAsync(endpoint, query, token.Value, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        token = await accessTokenProvider.RefreshAccessTokenAsync(token, cancellationToken);
        return await SendQueryAsync(endpoint, query, token.Value, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendQueryAsync(
        string endpoint,
        string query,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.igdb.com/v4/{endpoint}")
        {
            Content = new StringContent(query, Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("Client-ID", _options.ClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpClient.SendAsync(request, cancellationToken);
    }
}
