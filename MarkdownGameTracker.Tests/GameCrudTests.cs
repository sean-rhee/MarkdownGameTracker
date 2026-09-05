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


public sealed class GameCrudTests
{
    [Theory]
    [InlineData("Old (PC)", "New", "New (PC)")]
    [InlineData("Old", "New/Game", "New Game")]
    public async Task Rename_conflicts_preserve_both_notes(string sourceId, string title, string destinationId)
    {
        using var app = new TestApp();
        const string source = "---\ntype: game\ntitle: Old\nstatus: active\n---\nSource notes";
        const string destination = "---\ntype: game\nstatus: active\n---\nDestination notes";
        app.WriteGame(sourceId, source);
        app.WriteGame(destinationId, destination);

        var response = await app.Client.PutAsJsonAsync($"/api/games/{Uri.EscapeDataString(sourceId)}", new { title });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(source, await File.ReadAllTextAsync(app.GamePath(sourceId)));
        Assert.Equal(destination, await File.ReadAllTextAsync(app.GamePath(destinationId)));
    }

    [Fact]
    public async Task Rename_preserves_suffix_and_accepts_punctuation_in_display_title()
    {
        using var app = new TestApp();
        app.WriteGame("Old (PC)", "---\ntype: game\ntitle: Old\nstatus: active\n---\nKeep notes");

        var response = await app.Client.PutAsJsonAsync("/api/games/Old%20%28PC%29", new { title = "New/Game" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<GameNote>();
        Assert.NotNull(game);
        Assert.Equal("New Game (PC)", game.Id);
        Assert.Equal("New/Game", game.Title);
        Assert.Equal("Keep notes", game.Markdown);
        Assert.False(File.Exists(app.GamePath("Old (PC)")));
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
        Assert.Equal("Hades II", created.Frontmatter["title"]?.ToString());
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
    public async Task Display_titles_preserve_punctuation_while_filenames_stay_obsidian_friendly()
    {
        using var app = new TestApp();

        var response = await app.Client.PostAsJsonAsync(
            "/api/games",
            new CreateGameRequest(
                "Clair Obscur: Expedition 33",
                "completed",
                null,
                null,
                new Dictionary<string, object?> { ["platform"] = "PC" }));
        var game = await response.Content.ReadFromJsonAsync<GameNote>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(game);
        Assert.Equal("Clair Obscur - Expedition 33", game.Id);
        Assert.Equal("Clair Obscur: Expedition 33", game.Title);
        Assert.Equal("Clair Obscur: Expedition 33", game.Frontmatter["title"]?.ToString());
        Assert.True(game.Frontmatter.ContainsKey("aliases"));
        Assert.True(File.Exists(app.GamePath("Clair Obscur - Expedition 33")));
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
}
