using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarkdownGameTracker.Tests;


internal sealed class TestApp : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestApp()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"MarkdownGameTracker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(RootPath, "Games"));

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Vault:Path", RootPath);
                builder.UseSetting("Vault:GamesDirectory", "Games");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.RemoveAll<IIgdbDescriptionService>();
                    services.AddSingleton<IIgdbDescriptionService, FakeIgdbDescriptionService>();
                });
            });
        Client = _factory.CreateClient();
    }

    public string RootPath { get; }

    public HttpClient Client { get; }

    public string GamePath(string title) => Path.Combine(RootPath, "Games", $"{title}.md");

    public void WriteGame(string title, string contents) => File.WriteAllText(GamePath(title), contents);

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        Directory.Delete(RootPath, recursive: true);
    }
}

internal static class TestHelpers
{
    public static string GetAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}

internal sealed class FakeIgdbDescriptionService : IIgdbDescriptionService
{
    public Task<IgdbDescriptionResult> GetDescriptionAsync(
        string title,
        long? igdbGameId,
        CancellationToken cancellationToken) =>
        Task.FromResult(igdbGameId == 202
            ? IgdbDescriptionResult.Available(
                202,
                "The remastered description.",
                "Metadata Game Remastered",
                "https://www.igdb.com/games/metadata-game-remastered",
                "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/cover-remastered.jpg",
                "https://images.igdb.com/igdb/image/upload/t_1080p/artwork-remastered.jpg",
                [
                    new IgdbScreenshot("https://images.igdb.com/thumb-1.jpg", "https://images.igdb.com/full-1.jpg"),
                    new IgdbScreenshot("https://images.igdb.com/thumb-2.jpg", "https://images.igdb.com/full-2.jpg")
                ])
            : IgdbDescriptionResult.Available(
                101,
                $"{title} is a fetched description.",
                title,
                "https://www.igdb.com/games/metadata-game"));

    public Task<IgdbMatchSearchResult> SearchMatchesAsync(
        string title,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IgdbMatchSearchResult.Available(
        [
            new IgdbGameMatch(
                101,
                title,
                2020,
                "https://www.igdb.com/games/metadata-game"),
            new IgdbGameMatch(
                202,
                $"{title} Remastered",
                2024,
                "https://www.igdb.com/games/metadata-game-remastered",
                "https://images.igdb.com/igdb/image/upload/t_cover_small_2x/cover-remastered.jpg")
        ]));
    }

    public Task<IgdbCardArtworkResult> GetCardArtworkAsync(
        IReadOnlyList<IgdbArtworkLookup> games,
        CancellationToken cancellationToken) =>
        Task.FromResult(IgdbCardArtworkResult.Available(
            games.Select(game => new IgdbCardArtwork(
                game.GameId,
                game.IgdbGameId ?? 101,
                game.IgdbGameId == 202 ? $"{game.Title} Remastered" : game.Title,
                game.IgdbGameId == 202
                    ? "https://images.igdb.com/igdb/image/upload/t_720p/artwork-remastered.jpg"
                    : "https://images.igdb.com/igdb/image/upload/t_720p/artwork-default.jpg",
                "artwork"))
            .ToArray()));
}

internal sealed class FakeIgdbHttpHandler : HttpMessageHandler
{
    public int TokenRequestCount { get; private set; }

    public List<string> GameQueries { get; } = [];

    public List<string> MultiQueries { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host == "id.twitch.tv")
        {
            TokenRequestCount++;
            var tokenForm = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("client_id=test-client-id", tokenForm);
            Assert.Contains("client_secret=test-client-secret", tokenForm);
            Assert.Contains("grant_type=client_credentials", tokenForm);
            return JsonResponse("""
                {"access_token":"test-access-token","expires_in":3600,"token_type":"bearer"}
                """);
        }

        Assert.Equal("api.igdb.com", request.RequestUri?.Host);
        Assert.Equal("test-client-id", request.Headers.GetValues("Client-ID").Single());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-access-token", request.Headers.Authorization?.Parameter);
        var query = await request.Content!.ReadAsStringAsync(cancellationToken);

        if (request.RequestUri?.AbsolutePath.EndsWith("/multiquery", StringComparison.Ordinal) == true)
        {
            MultiQueries.Add(query);
            return JsonResponse("""
                [{
                  "name":"game0",
                  "result":[{
                    "id":101,
                    "name":"Metadata Game",
                    "cover":{"image_id":"cover101"},
                    "artworks":[{"image_id":"artwork101","width":1920,"height":1080}]
                  }]
                }]
                """);
        }

        GameQueries.Add(query);

        return query.Contains("where id = 202", StringComparison.Ordinal)
               || query.Contains("where id = (202)", StringComparison.Ordinal)
            ? JsonResponse("""
                [{
                  "id":202,
                  "name":"Metadata Game Remastered",
                  "summary":"The exact selected description.",
                  "slug":"metadata-game-remastered",
                  "cover":{"image_id":"cover202"},
                  "artworks":[{"image_id":"art202","width":1920,"height":1080}],
                  "screenshots":[
                    {"image_id":"shot202a","width":1920,"height":1080},
                    {"image_id":"shot202b","width":1920,"height":1080}
                  ]
                }]
                """)
            : query.Contains("where name = \"Metadata Game\"", StringComparison.Ordinal)
                ? JsonResponse("""
                    [{
                      "id":101,
                      "name":"Metadata Game",
                      "summary":"The exact title description.",
                      "slug":"metadata-game",
                      "first_release_date":1577836800,
                      "cover":{"image_id":"cover101"},
                      "artworks":[{"image_id":"art101","width":1920,"height":1080}],
                      "screenshots":[{"image_id":"shot101","width":1920,"height":1080}]
                    }]
                    """)
            : JsonResponse("""
                [{"id":202,"name":"Metadata Game Remastered","first_release_date":1704067200,"slug":"metadata-game-remastered","cover":{"image_id":"cover202"}}]
                """);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
