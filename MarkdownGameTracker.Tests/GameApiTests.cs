using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        Assert.True(paths.GetProperty("/api/markdown/preview").TryGetProperty("post", out _));

        var swaggerHtml = await swaggerResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"swagger-ui\"", swaggerHtml, StringComparison.OrdinalIgnoreCase);
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
                        services.AddDataProtection().UseEphemeralDataProtectionProvider());
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
}
