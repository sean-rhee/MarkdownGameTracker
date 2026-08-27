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

public sealed class GameApiTests
{
    [Fact]
    public async Task Razor_frontend_lists_games_and_exposes_crud_pages()
    {
        using var app = new TestApp();
        app.WriteGame(
            "Frontend Game",
            "---\ntype: game\nstatus: active\nrating: 8\n---\n## Thoughts\nLooks **lovely**.\n\n<script>alert('nope')</script>");

        var homeResponse = await app.Client.GetAsync("/");
        var detailsResponse = await app.Client.GetAsync("/Games/Details/Frontend%20Game");
        var editResponse = await app.Client.GetAsync("/Games/Edit/Frontend%20Game");
        var createResponse = await app.Client.GetAsync("/Games/Create");
        var homeHtml = await homeResponse.Content.ReadAsStringAsync();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        var createHtml = await createResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        Assert.True(detailsResponse.IsSuccessStatusCode, await detailsResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains("Game Garden", homeHtml);
        Assert.Contains("Frontend Game", homeHtml);
        Assert.Contains("Endless", homeHtml);
        Assert.Contains("Plan to play", homeHtml);
        Assert.Contains("status-tabs", homeHtml);
        Assert.Contains("#b3d9ff", homeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h2", detailsHtml);
        Assert.Contains("data-game-description", detailsHtml);
        Assert.Contains("data-game-media", detailsHtml);
        Assert.Contains("Choose another match", detailsHtml);
        Assert.Contains("not stored in your note", detailsHtml);
        Assert.Contains("<strong>lovely</strong>", detailsHtml);
        Assert.DoesNotContain("<script>alert('nope')</script>", detailsHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-markdown-editor", createHtml);
        Assert.Contains("data-markdown-action=\"bold\"", editHtml);
    }

    [Fact]
    public async Task Markdown_preview_renders_formatting_and_disables_raw_html()
    {
        using var app = new TestApp();

        var response = await app.Client.PostAsJsonAsync(
            "/api/markdown/preview",
            new
            {
                markdown = "## Preview\n\n- one\n- two\n\n**Bold**\n\n[unsafe](javascript:alert('nope'))\n\n<script>alert('nope')</script>"
            });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h2", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("<strong>Bold</strong>", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#\"", html);
    }

    [Fact]
    public async Task Home_status_menu_updates_the_markdown_frontmatter()
    {
        using var app = new TestApp();
        app.WriteGame("Move Me", "---\ntype: game\nstatus: active\n---\n## Notes");

        var homeHtml = await app.Client.GetStringAsync("/?status=active");
        var tokenMatch = Regex.Match(
            homeHtml,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(tokenMatch.Success);

        var response = await app.Client.PostAsync(
            "/?handler=ChangeStatus",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value),
                ["id"] = "Move Me",
                ["status"] = "endless",
                ["currentStatus"] = "active",
                ["search"] = string.Empty
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var savedNote = await File.ReadAllTextAsync(app.GamePath("Move Me"));
        Assert.Contains("status: endless", savedNote);
        Assert.DoesNotContain("<h3>Move Me</h3>", await response.Content.ReadAsStringAsync());
        Assert.Contains("Move Me", await app.Client.GetStringAsync("/?status=endless"));
    }

    [Fact]
    public async Task OpenApi_document_and_swagger_ui_describe_the_crud_api()
    {
        using var app = new TestApp();

        var openApiResponse = await app.Client.GetAsync("/openapi/v1.json");
        var swaggerResponse = await app.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);

        using var document = JsonDocument.Parse(await openApiResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Markdown Game Tracker API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal("List game notes", paths.GetProperty("/api/games").GetProperty("get").GetProperty("summary").GetString());
        Assert.True(paths.GetProperty("/api/games").GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty("/api/games/{id}/igdb-description").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/{id}/igdb-matches").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/markdown/preview").TryGetProperty("post", out _));

        var swaggerHtml = await swaggerResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"swagger-ui\"", swaggerHtml, StringComparison.OrdinalIgnoreCase);
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
    public async Task Igdb_service_searches_candidates_and_loads_the_selected_game_id()
    {
        using var handler = new FakeIgdbHttpHandler();
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new IgdbDescriptionService(
            client,
            Options.Create(new IgdbOptions
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret"
            }),
            cache,
            NullLogger<IgdbDescriptionService>.Instance);

        var matches = await service.SearchMatchesAsync("Metadata Game", CancellationToken.None);
        var description = await service.GetDescriptionAsync("Metadata Game", 202, CancellationToken.None);

        Assert.Equal(IgdbDescriptionResult.AvailableStatus, matches.Status);
        Assert.Equal(2, matches.Matches.Count);
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
        Assert.Equal(2, handler.GameQueries.Count);
        Assert.Contains("search \"Metadata Game\"", handler.GameQueries[0]);
        Assert.Contains("where id = 202", handler.GameQueries[1]);
    }

    [Fact]
    public async Task Get_reads_an_existing_obsidian_note()
    {
        using var app = new TestApp();
        app.WriteGame(
            "Persona 3",
            """
            ---
            type: game
            hobby:
              - "[[Gaming]]"
            status: completed
            rating: 9.5
            ---
            ## Thoughts
            Great ending.
            """);

        var game = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Persona%203");

        Assert.NotNull(game);
        Assert.Equal("Persona 3", game.Title);
        Assert.Equal("completed", game.Status);
        Assert.Equal(9.5m, game.Rating);
        Assert.Contains("Great ending.", game.Markdown);
        Assert.Equal("game", game.Frontmatter["type"]?.ToString());
    }

    [Fact]
    public async Task List_filters_by_status_and_searches_markdown()
    {
        using var app = new TestApp();
        app.WriteGame("Active Game", "---\ntype: game\nstatus: active\n---\n## Notes\nSpace adventure");
        app.WriteGame("Completed Game", "---\ntype: game\nstatus: completed\n---\nSpace mystery");
        app.WriteGame("Other", "---\ntype: game\nstatus: active\n---\nFantasy");
        app.WriteGame("Endless Game", "---\ntype: game\nstatus: endless\n---\nAlways another round");
        app.WriteGame("Future Game", "---\ntype: game\nstatus: plan to play\n---\nSomeday");
        app.WriteGame("Games", "---\ntype: index\ncollection: games\n---\n# Games");

        var byStatus = await app.Client.GetFromJsonAsync<GameNote[]>("/api/games?status=active");
        var byEndless = await app.Client.GetFromJsonAsync<GameNote[]>("/api/games?status=endless");
        var byPlanned = await app.Client.GetFromJsonAsync<GameNote[]>("/api/games?status=planned");
        var bySearch = await app.Client.GetFromJsonAsync<GameNote[]>("/api/games?search=space");

        Assert.Equal(["Active Game", "Other"], byStatus!.Select(game => game.Title));
        Assert.Equal("endless", Assert.Single(byEndless!).Status);
        Assert.Equal("planned", Assert.Single(byPlanned!).Status);
        Assert.Equal(["Active Game", "Completed Game"], bySearch!.Select(game => game.Title));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/api/games/Games")).StatusCode);
    }

    [Fact]
    public async Task Create_update_rename_and_delete_round_trip_through_markdown()
    {
        using var app = new TestApp();
        var createRequest = new CreateGameRequest(
            "Hades II",
            "endless",
            8.5m,
            "## Thoughts\nReady for another run.",
            new Dictionary<string, object?>
            {
                ["platforms"] = new[] { "PC", "Steam Deck" }
            });

        var createResponse = await app.Client.PostAsJsonAsync("/api/games", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<GameNote>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.True(File.Exists(app.GamePath("Hades II")));
        Assert.Equal("game", created.Frontmatter["type"]?.ToString());
        Assert.Equal("endless", created.Status);
        Assert.Contains("status: endless", await File.ReadAllTextAsync(app.GamePath("Hades II")));

        var updateRequest = new UpdateGameRequest(
            "Hades 2",
            "active",
            9m,
            "## Thoughts\nOne more run.",
            null);
        var updateResponse = await app.Client.PutAsJsonAsync("/api/games/Hades%20II", updateRequest);
        var updated = await updateResponse.Content.ReadFromJsonAsync<GameNote>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal("Hades 2", updated.Title);
        Assert.Equal("active", updated.Status);
        Assert.Equal(9m, updated.Rating);
        Assert.True(updated.Frontmatter.ContainsKey("platforms"));
        Assert.False(File.Exists(app.GamePath("Hades II")));
        Assert.True(File.Exists(app.GamePath("Hades 2")));

        var savedMarkdown = await File.ReadAllTextAsync(app.GamePath("Hades 2"));
        Assert.StartsWith("---", savedMarkdown);
        Assert.Contains("status: active", savedMarkdown);
        Assert.Contains("## Thoughts", savedMarkdown);

        var deleteResponse = await app.Client.DeleteAsync("/api/games/Hades%202");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(app.GamePath("Hades 2")));
    }

    [Fact]
    public async Task Create_rejects_duplicates_invalid_ratings_and_unsafe_titles()
    {
        using var app = new TestApp();
        app.WriteGame("Existing", "---\ntype: game\n---\n");

        var duplicate = await app.Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameRequest("existing", "active", null, null, null));
        var invalidRating = await app.Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameRequest("New Game", "active", 10.1m, null, null));
        var unsafeTitle = await app.Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameRequest("../outside", "active", null, null, null));
        var invalidStatus = await app.Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameRequest("Paused Game", "paused", null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRating.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        Assert.False(File.Exists(Path.Combine(app.RootPath, "outside.md")));
    }

    private sealed class TestApp : IDisposable
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

    private sealed class FakeIgdbDescriptionService : IIgdbDescriptionService
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
            CancellationToken cancellationToken) =>
            Task.FromResult(IgdbMatchSearchResult.Available(
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

    private sealed class FakeIgdbHttpHandler : HttpMessageHandler
    {
        public int TokenRequestCount { get; private set; }

        public List<string> GameQueries { get; } = [];

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
            GameQueries.Add(query);

            return query.Contains("where id = 202", StringComparison.Ordinal)
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
                : JsonResponse("""
                    [
                      {"id":101,"name":"Metadata Game","first_release_date":1577836800,"slug":"metadata-game","cover":{"image_id":"cover101"}},
                      {"id":202,"name":"Metadata Game Remastered","first_release_date":1704067200,"slug":"metadata-game-remastered","cover":{"image_id":"cover202"}}
                    ]
                    """);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
