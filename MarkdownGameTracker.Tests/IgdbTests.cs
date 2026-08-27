using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Api;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarkdownGameTracker.Tests;


public sealed class IgdbTests
{
    [Fact]
    public async Task Igdb_title_suggestions_do_not_require_an_existing_game_note()
    {
        using var app = new TestApp();

        var response = await app.Client.GetAsync("/api/games/igdb-title-suggestions?query=Hollow");
        var result = await response.Content.ReadFromJsonAsync<IgdbMatchSearchResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(IgdbDescriptionResult.AvailableStatus, result.Status);
        Assert.Equal("Hollow", result.Matches[0].Title);
        Assert.Equal("Hollow Remastered", result.Matches[1].Title);

        var shortQueryResponse = await app.Client.GetAsync("/api/games/igdb-title-suggestions?query=H");
        Assert.Equal(HttpStatusCode.BadRequest, shortQueryResponse.StatusCode);
    }

    [Fact]
    public async Task Canceled_Igdb_title_suggestions_are_treated_as_a_closed_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await GameMetadataEndpoints.SearchIgdbTitleSuggestionsAsync(
            "Hollow",
            new FakeIgdbDescriptionService(),
            cancellation.Token);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, statusResult.StatusCode);
    }

    [Fact]
    public async Task Igdb_description_is_loaded_separately_without_changing_the_note()
    {
        using var app = new TestApp();
        const string originalNote = "---\ntype: game\nstatus: active\n---\n## Personal notes\nDo not change me.";
        app.WriteGame("Metadata Game", originalNote);

        var detailsHtml = await app.Client.GetStringAsync("/Games/Details/Metadata%20Game");
        var result = await app.Client.GetFromJsonAsync<IgdbDescriptionResult>(
            "/api/games/Metadata%20Game/igdb-description");
        var matches = await app.Client.GetFromJsonAsync<IgdbMatchSearchResult>(
            "/api/games/Metadata%20Game/igdb-matches");
        var selectedResult = await app.Client.GetFromJsonAsync<IgdbDescriptionResult>(
            "/api/games/Metadata%20Game/igdb-description?igdbId=202");

        Assert.Contains("/api/games/Metadata%20Game/igdb-description", detailsHtml);
        Assert.Contains("/api/games/Metadata%20Game/igdb-matches", detailsHtml);
        Assert.DoesNotContain("Metadata Game is a fetched description.", detailsHtml);
        Assert.NotNull(result);
        Assert.Equal(IgdbDescriptionResult.AvailableStatus, result.Status);
        Assert.Equal("Metadata Game is a fetched description.", result.Description);
        Assert.Equal("Metadata Game", result.MatchedTitle);
        Assert.Equal(101, result.IgdbGameId);
        Assert.NotNull(matches);
        Assert.Equal(2, matches.Matches.Count);
        Assert.Equal("Metadata Game Remastered", matches.Matches[1].Title);
        Assert.Equal(2024, matches.Matches[1].ReleaseYear);
        Assert.NotNull(selectedResult);
        Assert.Equal(202, selectedResult.IgdbGameId);
        Assert.Equal("Metadata Game Remastered", selectedResult.MatchedTitle);
        Assert.Equal("The remastered description.", selectedResult.Description);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/cover-remastered.jpg",
            selectedResult.CoverUrl);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_1080p/artwork-remastered.jpg",
            selectedResult.HeroUrl);
        Assert.Equal(2, selectedResult.Screenshots.Count);
        Assert.Equal(originalNote, await File.ReadAllTextAsync(app.GamePath("Metadata Game")));

        var missingResponse = await app.Client.GetAsync("/api/games/Missing/igdb-description");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task Igdb_card_artwork_is_loaded_in_a_batch_without_changing_notes()
    {
        using var app = new TestApp();
        const string originalNote = "---\ntype: game\nstatus: active\n---\nMy notes stay local.";
        app.WriteGame("Metadata Game", originalNote);

        var response = await app.Client.PostAsJsonAsync(
            "/api/games/igdb-card-artwork",
            new
            {
                games = new[]
                {
                    new { id = "Metadata Game", igdbGameId = (long?)202 }
                }
            });
        var result = await response.Content.ReadFromJsonAsync<IgdbCardArtworkResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var artwork = Assert.Single(result.Artwork);
        Assert.Equal("Metadata Game", artwork.GameId);
        Assert.Equal(202, artwork.IgdbGameId);
        Assert.Equal("artwork", artwork.Kind);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_720p/artwork-remastered.jpg",
            artwork.ArtworkUrl);
        Assert.Equal(originalNote, await File.ReadAllTextAsync(app.GamePath("Metadata Game")));
    }

    [Fact]
    public async Task Igdb_service_searches_candidates_and_loads_the_selected_game_id()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        var matches = await service.SearchMatchesAsync("Metadata Game", CancellationToken.None);
        var description = await service.GetDescriptionAsync("Metadata Game", 202, CancellationToken.None);

        Assert.Equal(IgdbDescriptionResult.AvailableStatus, matches.Status);
        Assert.Equal(2, matches.Matches.Count);
        Assert.Equal("Metadata Game", matches.Matches[0].Title);
        Assert.Equal(2024, matches.Matches[1].ReleaseYear);
        Assert.Equal(202, description.IgdbGameId);
        Assert.Equal("Metadata Game Remastered", description.MatchedTitle);
        Assert.Equal("The exact selected description.", description.Description);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/cover202.jpg",
            description.CoverUrl);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_1080p/art202.jpg",
            description.HeroUrl);
        Assert.Equal(2, description.Screenshots.Count);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_small_2x/cover202.jpg",
            matches.Matches[1].CoverUrl);
        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(3, handler.GameQueries.Count);
        Assert.Contains("where name = \"Metadata Game\"", handler.GameQueries[0]);
        Assert.Contains("search \"Metadata Game\"", handler.GameQueries[1]);
        Assert.Contains("where version_parent = null", handler.GameQueries[1]);
        Assert.Contains("where id = 202", handler.GameQueries[2]);
    }

    [Fact]
    public async Task Igdb_service_uses_an_exact_title_before_similarity_results_for_description()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        var result = await service.GetDescriptionAsync("Metadata Game", null, CancellationToken.None);

        Assert.Equal(IgdbDescriptionResult.AvailableStatus, result.Status);
        Assert.Equal(101, result.IgdbGameId);
        Assert.Equal("Metadata Game", result.MatchedTitle);
        Assert.Equal("The exact title description.", result.Description);
        Assert.Single(handler.GameQueries);
        Assert.Contains("where name = \"Metadata Game\"", handler.GameQueries[0]);
        Assert.DoesNotContain("search", handler.GameQueries[0]);
    }

    [Fact]
    public async Task Igdb_service_batches_selected_and_title_matched_card_artwork()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        var result = await service.GetCardArtworkAsync(
            [
                new IgdbArtworkLookup("Metadata Game", "Metadata Game", null),
                new IgdbArtworkLookup("Chosen Game", "Wrong Local Title", 202)
            ],
            CancellationToken.None);

        Assert.Equal(IgdbDescriptionResult.AvailableStatus, result.Status);
        Assert.Equal(2, result.Artwork.Count);
        Assert.Contains(result.Artwork, item =>
            item.GameId == "Metadata Game"
            && item.ArtworkUrl.EndsWith("/artwork101.jpg", StringComparison.Ordinal));
        Assert.Contains(result.Artwork, item =>
            item.GameId == "Chosen Game"
            && item.IgdbGameId == 202
            && item.ArtworkUrl.EndsWith("/art202.jpg", StringComparison.Ordinal));
        Assert.Single(handler.GameQueries);
        Assert.Contains("where id = (202)", handler.GameQueries[0]);
        Assert.Single(handler.MultiQueries);
        Assert.Contains("query games \"game0\"", handler.MultiQueries[0]);
        Assert.Contains("where name = \"Metadata Game\"", handler.MultiQueries[0]);
        Assert.Equal(1, handler.TokenRequestCount);
    }

    [Fact]
    public async Task Igdb_access_token_is_shared_across_transient_service_instances()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        using var firstCache = new MemoryCache(new MemoryCacheOptions());
        using var secondCache = new MemoryCache(new MemoryCacheOptions());
        var options = CreateIgdbOptions();
        var accessTokenProvider = new IgdbAccessTokenProvider(
            new FakeHttpClientFactory(client),
            options);
        var firstService = CreateService(client, firstCache, options, accessTokenProvider);
        var secondService = CreateService(client, secondCache, options, accessTokenProvider);

        await firstService.SearchMatchesAsync("Metadata Game", CancellationToken.None);
        await secondService.GetDescriptionAsync("Metadata Game", 202, CancellationToken.None);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(3, handler.GameQueries.Count);
    }

    [Fact]
    public async Task Concurrent_unauthorized_refreshes_replace_a_token_only_once()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        var provider = new IgdbAccessTokenProvider(
            new FakeHttpClientFactory(client),
            CreateIgdbOptions());
        var rejectedToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        var refreshedTokens = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => provider.RefreshAccessTokenAsync(rejectedToken, CancellationToken.None)));

        Assert.Equal(2, handler.TokenRequestCount);
        Assert.All(refreshedTokens, token => Assert.Equal(refreshedTokens[0].Generation, token.Generation));
        Assert.NotEqual(rejectedToken.Generation, refreshedTokens[0].Generation);
    }

    private static IgdbDescriptionService CreateService(HttpClient client, IMemoryCache cache)
    {
        var options = CreateIgdbOptions();
        var accessTokenProvider = new IgdbAccessTokenProvider(
            new FakeHttpClientFactory(client),
            options);
        return CreateService(client, cache, options, accessTokenProvider);
    }

    private static IgdbDescriptionService CreateService(
        HttpClient client,
        IMemoryCache cache,
        IOptions<IgdbOptions> options,
        IIgdbAccessTokenProvider accessTokenProvider)
    {
        var apiClient = new IgdbApiClient(
            client,
            options,
            accessTokenProvider,
            NullLogger<IgdbApiClient>.Instance);
        return new IgdbDescriptionService(apiClient, options, cache);
    }

    private static IOptions<IgdbOptions> CreateIgdbOptions() =>
        Options.Create(new IgdbOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        });
}
