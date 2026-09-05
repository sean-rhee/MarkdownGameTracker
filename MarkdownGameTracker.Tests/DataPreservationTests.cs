using System.Net;
using System.Net.Http.Json;
using MarkdownGameTracker.Models;
using Xunit;

namespace MarkdownGameTracker.Tests;

public sealed class DataPreservationTests
{
    private const string Note = "---\ntype: game\ntitle: Protected\nstatus: active\nrating: 7\ncustom:\n  tags: [one, two]\n  enabled: true\nplatform: PC\n---\n## Journal\nKeep **every** word.\n";

    [Theory]
    [InlineData("{\"rating\":11}")]
    [InlineData("{\"status\":\"unsupported\"}")]
    [InlineData("{\"title\":\"../outside\"}")]
    [InlineData("{\"title\":\"\"}")]
    public async Task Rejected_updates_leave_file_bytes_unchanged(string json)
    {
        using var app = new TestApp();
        app.WriteGame("Protected", Note);
        var before = await File.ReadAllBytesAsync(app.GamePath("Protected"));
        using var response = await app.Client.PutAsync("/api/games/Protected",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(app.GamePath("Protected")));
        Assert.Single(Directory.GetFiles(Path.Combine(app.RootPath, "Games")));
    }

    [Fact]
    public async Task Concurrent_partial_updates_preserve_notes_and_unknown_frontmatter()
    {
        using var app = new TestApp();
        app.WriteGame("Protected", Note);
        var before = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Protected");
        var responses = await Task.WhenAll(
            app.Client.PutAsJsonAsync("/api/games/Protected", new { status = "completed" }),
            app.Client.PutAsJsonAsync("/api/games/Protected", new { rating = 9.5m }),
            app.Client.PutAsJsonAsync("/api/games/Protected", new { frontmatter = new { platform = (string?)null } }));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        var saved = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Protected");
        Assert.NotNull(before);
        Assert.NotNull(saved);
        Assert.Equal("completed", saved.Status);
        Assert.Equal(9.5m, saved.Rating);
        Assert.Equal(before.Markdown, saved.Markdown);
        Assert.Equal(before.Frontmatter["custom"]!.ToString(), saved.Frontmatter["custom"]!.ToString());
        Assert.False(saved.Frontmatter.ContainsKey("platform"));
    }

    [Fact]
    public async Task Concurrent_creates_have_one_winner_without_overwriting_its_notes()
    {
        using var app = new TestApp();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            app.Client.PostAsJsonAsync("/api/games", new { title = "Contended", status = "active", markdown = $"Writer {index}" })));
        var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var created = await winner.Content.ReadFromJsonAsync<GameNote>();
        var saved = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Contended");
        Assert.NotNull(created);
        Assert.NotNull(saved);
        Assert.Equal(created.Markdown, saved.Markdown);
        Assert.Single(Directory.GetFiles(Path.Combine(app.RootPath, "Games")));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Non_game_notes_cannot_be_updated_deleted_or_replaced()
    {
        using var app = new TestApp();
        const string index = "---\ntype: index\n---\n# My game index\n";
        app.WriteGame("Games", index);

        using var update = await app.Client.PutAsJsonAsync("/api/games/Games", new { markdown = "replacement" });
        using var delete = await app.Client.DeleteAsync("/api/games/Games");
        using var create = await app.Client.PostAsJsonAsync("/api/games", new { title = "Games", status = "active" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(index, await File.ReadAllTextAsync(app.GamePath("Games")));
    }
}
