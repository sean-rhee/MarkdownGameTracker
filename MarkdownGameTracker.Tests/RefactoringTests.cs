using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Xunit;

namespace MarkdownGameTracker.Tests;

public sealed class RefactoringTests
{
    [Theory]
    [InlineData("{\"frontmatter\":{\"rating\":99}}")]
    [InlineData("{\"frontmatter\":{\"Rating\":-1}}")]
    [InlineData("{\"frontmatter\":{\"rating\":\"NaN\"}}")]
    [InlineData("{\"frontmatter\":{\"rating\":true}}")]
    [InlineData("{\"frontmatter\":{\"rating\":{\"value\":9}}}")]
    [InlineData("{\"rating\":8,\"clearRating\":true}")]
    [InlineData("{\"frontmatter\":{\"rating\":8},\"clearRating\":true}")]
    public async Task Invalid_rating_updates_preserve_the_original_bytes(string json)
    {
        using var app = new TestApp();
        const string original = "---\ntype: game\nstatus: active\nrating: 7\ncustom: keep\n---\nJournal";
        app.WriteGame("Protected", original);
        using var response = await app.Client.PutAsync("/api/games/Protected",
            new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, await File.ReadAllTextAsync(app.GamePath("Protected")));
    }

    [Fact]
    public async Task Rating_supports_unchanged_set_and_clear_in_both_request_formats()
    {
        using var app = new TestApp();
        var create = await app.Client.PostAsJsonAsync("/api/games", new
        {
            title = "Rating", status = "active", markdown = "Keep notes", frontmatter = new { rating = 8.5m, custom = "keep" }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        foreach (var (json, expected) in new (string, decimal?)[]
        {
            ("{\"rating\":null}", 8.5m),
            ("{\"clearRating\":true}", null),
            ("{\"frontmatter\":{\"Rating\":\"9.5\"}}", 9.5m),
            ("{\"frontmatter\":{\"rating\":null}}", null),
            ("{\"rating\":0}", 0)
        })
        {
            using var response = await app.Client.PutAsync("/api/games/Rating",
                new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var game = await response.Content.ReadFromJsonAsync<GameNote>();
            Assert.NotNull(game);
            Assert.Equal(expected, game.Rating);
            Assert.Equal("Keep notes", game.Markdown);
            Assert.Equal("keep", game.Frontmatter["custom"]!.ToString());
            Assert.DoesNotContain("Rating", game.Frontmatter.Keys);
        }
        var invalidCreate = await app.Client.PostAsJsonAsync("/api/games", new
        {
            title = "Invalid", status = "active", frontmatter = new { rating = 99 }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
        Assert.False(File.Exists(app.GamePath("Invalid")));
    }

    [Theory]
    [InlineData("Clair Obscur: Expedition 33", "Clair Obscur - Expedition 33")]
    [InlineData("Game<>\"/\\|?*", "Game")]
    [InlineData("CON.txt", "Game - CON.txt")]
    [InlineData("NUL", "Game - NUL")]
    public void Filenames_are_portable_on_every_host(string title, string expected) =>
        Assert.Equal(expected, GameNoteFileNames.CreateSafeId(title));

    [Fact]
    public async Task Case_only_rename_keeps_exactly_one_file_with_the_requested_spelling()
    {
        using var app = new TestApp();
        app.WriteGame("Mixed (PC)", "---\ntype: game\ntitle: Mixed\nstatus: active\ncustom: keep\n---\nKeep notes");
        var response = await app.Client.PutAsJsonAsync("/api/games/Mixed%20%28PC%29", new { title = "MIXED" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await response.Content.ReadFromJsonAsync<GameNote>();
        Assert.NotNull(game);
        Assert.Equal("MIXED (PC)", game.Id);
        Assert.Equal("Keep notes", game.Markdown);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(app.RootPath, "Games")));
        Assert.Equal("MIXED (PC).md", Path.GetFileName(file));
        var caseInsensitiveRead = await app.Client.GetFromJsonAsync<GameNote>("/api/games/mixed%20%28pc%29");
        Assert.Equal(game.Id, caseInsensitiveRead!.Id);
    }

    [Fact]
    public async Task Broken_notes_are_reported_without_hiding_healthy_notes_or_allowing_writes()
    {
        using var app = new TestApp();
        app.WriteGame("Healthy", "---\ntype: game\nstatus: active\n---\nJournal");
        const string broken = "---\ntype: game\nstatus: [unclosed\n---\nKeep broken file";
        app.WriteGame("Broken", broken);
        app.WriteGame("Index", "---\ntype: index\n---\nIndex");
        var scan = await app.Client.GetFromJsonAsync<GameLibraryScan>("/api/games/scan");
        Assert.NotNull(scan);
        Assert.Equal("Healthy", Assert.Single(scan.Games).Id);
        Assert.Equal("Broken.md", Assert.Single(scan.Diagnostics).FileName);
        Assert.Single((await app.Client.GetFromJsonAsync<GameNote[]>("/api/games"))!);
        var page = await app.Client.GetStringAsync("/");
        Assert.Contains("Some notes could not be loaded", page);
        Assert.Contains("Broken.md", page);
        Assert.Contains("data-game-id=\"Healthy\"", page);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await app.Client.PutAsJsonAsync("/api/games/Broken", new { markdown = "Replace" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await app.Client.DeleteAsync("/api/games/Broken")).StatusCode);
        Assert.Equal(broken, await File.ReadAllTextAsync(app.GamePath("Broken")));
    }

    [Theory]
    [InlineData(" pc ")]
    [InlineData("café")]
    [InlineData("straße")]
    public async Task Api_and_initial_html_search_the_same_fields(string query)
    {
        using var app = new TestApp();
        app.WriteGame("Matching", "---\ntype: game\nstatus: active\nplatform: PC Café Straße\n---\nJournal");
        app.WriteGame("Other", "---\ntype: game\nstatus: active\nplatform: Console\n---\nJournal");
        var encoded = Uri.EscapeDataString(query);
        var games = await app.Client.GetFromJsonAsync<GameNote[]>($"/api/games?search={encoded}");
        Assert.Equal("Matching", Assert.Single(games!).Id);
        var page = await app.Client.GetStringAsync($"/?search={encoded}");
        var card = Regex.Match(page, "<article[^>]*data-game-id=\"Matching\"[^>]*>", RegexOptions.Singleline);
        Assert.True(card.Success);
        Assert.DoesNotContain("hidden", card.Value);
        Assert.Contains("1 result", Regex.Replace(page, @"\s+", " "));
    }
}
