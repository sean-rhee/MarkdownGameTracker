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


public sealed class FrontendTests
{
    [Theory]
    [InlineData("title", "Alpha,Beta,Gamma,Zero")]
    [InlineData("title-desc", "Zero,Gamma,Beta,Alpha")]
    [InlineData("rating", "Beta,Gamma,Zero,Alpha")]
    [InlineData("updated", "Gamma,Zero,Alpha,Beta")]
    [InlineData("invalid", "Alpha,Beta,Gamma,Zero")]
    public async Task Library_sorts_notes_and_remembers_the_choice(string sort, string expected)
    {
        using var app = new TestApp();
        foreach (var (title, rating, day) in new[]
                 { ("Alpha", "", 2), ("Beta", "rating: 9\n", 1), ("Gamma", "rating: 9\n", 4), ("Zero", "rating: 0\n", 3) })
        {
            app.WriteGame(title, $"---\ntype: game\nstatus: active\n{rating}---\nShared notes");
            File.SetLastWriteTimeUtc(app.GamePath(title), new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }

        static string CardOrder(string html) => string.Join(",", Regex.Matches(html, "data-game-id=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value));
        var html = await app.Client.GetStringAsync($"/?sort={sort}&search=Shared");
        Assert.Equal(expected, CardOrder(html));
        Assert.Equal(expected, CardOrder(await app.Client.GetStringAsync("/")));
        var selected = sort == "invalid" ? "title" : sort;
        Assert.Contains($"sort={selected}", html);

        // Inline edits retain this page's sort even if the browser preference changes.
        await app.Client.GetStringAsync("/?sort=title-desc");
        var response = await app.Client.PostAsync("/?handler=ChangeRating", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TestHelpers.GetAntiforgeryToken(html),
            ["id"] = "Alpha", ["rating"] = "8", ["currentStatus"] = "active",
            ["search"] = "Shared", ["sort"] = selected
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"sort={selected}", response.RequestMessage!.RequestUri!.Query);
        Assert.Contains("search=Shared", response.RequestMessage.RequestUri.Query);
    }

    [Theory]
    [InlineData("![image](missing.png){onerror=alert(1)}")]
    [InlineData("[link](https://example.com){onclick=alert(1)}")]
    [InlineData("# Heading {onmouseover=alert(1)}")]
    public async Task Markdown_attributes_cannot_create_event_handlers(string markdown)
    {
        using var app = new TestApp();
        app.WriteGame("Attributes", $"---\ntype: game\nstatus: active\n---\n{markdown}");

        var response = await app.Client.PostAsJsonAsync("/api/markdown/preview", new { markdown });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadAsStringAsync();
        var details = await app.Client.GetStringAsync("/Games/Details/Attributes");

        Assert.DoesNotMatch(@"<[^>]+\son(?:error|click|mouseover)\s*=", preview);
        Assert.DoesNotMatch(@"<[^>]+\son(?:error|click|mouseover)\s*=", details);
    }

    [Fact]
    public async Task Clearing_the_editor_removes_saved_markdown()
    {
        using var app = new TestApp();
        app.WriteGame("Clear Me", "---\ntype: game\nstatus: active\n---\nOld notes");
        var html = await app.Client.GetStringAsync("/Games/Edit/Clear%20Me");
        var response = await app.Client.PostAsync("/Games/Edit/Clear%20Me", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TestHelpers.GetAntiforgeryToken(html),
            ["Input.Title"] = "Clear Me",
            ["Input.Status"] = "active",
            ["Input.Markdown"] = string.Empty
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var game = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Clear%20Me");
        Assert.NotNull(game);
        Assert.Equal(string.Empty, game.Markdown);

        await app.Client.PutAsJsonAsync("/api/games/Clear%20Me", new { markdown = "API notes" });
        var update = await app.Client.PutAsJsonAsync("/api/games/Clear%20Me", new { status = "completed" });
        var updated = await update.Content.ReadFromJsonAsync<GameNote>();
        Assert.NotNull(updated);
        Assert.Equal("API notes", updated.Markdown);
    }

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
        var libraryCssResponse = await app.Client.GetAsync("/css/game-library.css");
        var homeHtml = await homeResponse.Content.ReadAsStringAsync();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        var createHtml = await createResponse.Content.ReadAsStringAsync();
        var libraryCss = await libraryCssResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, homeResponse.StatusCode);
        Assert.True(detailsResponse.IsSuccessStatusCode, await detailsResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, libraryCssResponse.StatusCode);
        Assert.Contains("Game Garden", homeHtml);
        Assert.Contains("Frontend Game", homeHtml);
        Assert.Contains("Endless", homeHtml);
        Assert.Contains("Plan to play", homeHtml);
        Assert.Contains("status-tabs", homeHtml);
        Assert.Contains("data-game-artwork-grid", homeHtml);
        Assert.Contains("data-game-id=\"Frontend Game\"", homeHtml);
        Assert.Contains("game-card-artwork.js", homeHtml);
        Assert.Contains("aria-label=\"Change rating for Frontend Game\"", homeHtml);
        Assert.Contains("#b3d9ff", homeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h2", detailsHtml);
        Assert.Contains("data-game-description", detailsHtml);
        Assert.Contains("data-game-media", detailsHtml);
        Assert.Contains("data-game-media-collection", detailsHtml);
        Assert.Contains("game-details-strip", detailsHtml);
        Assert.Contains("game-hero-title", detailsHtml);
        Assert.Contains("data-has-hero=\"false\"", detailsHtml);
        Assert.Contains("data-game-screenshot-viewer", detailsHtml);
        Assert.Contains("data-screenshot-previous", detailsHtml);
        Assert.Contains("data-screenshot-next", detailsHtml);
        Assert.Contains("data-screenshot-counter", detailsHtml);
        Assert.Contains("data-game-video", detailsHtml);
        Assert.Contains("data-game-video-carousel", detailsHtml);
        Assert.Contains("data-video-count", detailsHtml);
        Assert.True(
            detailsHtml.IndexOf("data-game-video", StringComparison.Ordinal)
            < detailsHtml.IndexOf("data-game-screenshots", StringComparison.Ordinal));
        Assert.Contains("Choose another match", detailsHtml);
        Assert.Contains("not stored in your note", detailsHtml);
        Assert.Contains("markdown-edit-link", detailsHtml);
        Assert.Contains("<strong>lovely</strong>", detailsHtml);
        Assert.DoesNotContain("<script>alert('nope')</script>", detailsHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-markdown-editor", createHtml);
        Assert.Contains("data-game-title-suggestions", createHtml);
        Assert.Contains("/api/games/igdb-title-suggestions", createHtml);
        Assert.Contains("game-title-suggestions.js", createHtml);
        Assert.Contains("data-markdown-action=\"bold\"", editHtml);
        Assert.Contains(".game-card:has(.dropdown-menu.show)", libraryCss);
        Assert.Contains("z-index: 10", libraryCss);
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
    public async Task Home_search_suggestions_are_scoped_to_the_selected_status()
    {
        using var app = new TestApp();
        app.WriteGame("Active Suggestion", "---\ntype: game\nstatus: active\n---\n## Notes");
        app.WriteGame("Completed Suggestion", "---\ntype: game\nstatus: completed\n---\n## Notes");

        var homeHtml = await app.Client.GetStringAsync("/?status=active");

        Assert.Contains("role=\"combobox\"", homeHtml);
        Assert.Contains("aria-controls=\"searchSuggestions\"", homeHtml);
        Assert.Contains("data-search-value=\"Active Suggestion\"", homeHtml);
        Assert.DoesNotContain("data-search-value=\"Completed Suggestion\"", homeHtml);
        Assert.Contains("search-suggestions.js", homeHtml);
    }

    [Fact]
    public async Task Home_search_renders_the_status_library_for_instant_title_and_note_filtering()
    {
        using var app = new TestApp();
        app.WriteGame("Dark Souls III", "---\ntype: game\nstatus: active\n---\n## Notes\nReturn to Firelink Shrine.");
        app.WriteGame("Lantern", "---\ntype: game\nstatus: active\n---\n## Notes\nExplore the dark catacombs.");
        app.WriteGame("Celeste", "---\ntype: game\nstatus: active\n---\n## Notes\nClimb the mountain.");
        app.WriteGame("Dark Completed", "---\ntype: game\nstatus: completed\n---\n## Notes");

        var homeHtml = await app.Client.GetStringAsync("/?status=active&search=dark");

        Assert.Contains("2 results", homeHtml);
        Assert.Contains("data-search-text=", homeHtml);
        Assert.Contains("<h3>Dark Souls III</h3>", homeHtml);
        Assert.Contains("<h3>Lantern</h3>", homeHtml);
        Assert.Contains("<h3>Celeste</h3>", homeHtml);
        Assert.DoesNotContain("<h3>Dark Completed</h3>", homeHtml);

        var titleMatchCard = Regex.Match(
            homeHtml,
            "<article(?=[^>]*data-game-id=\"Dark Souls III\")[^>]*>");
        var noteMatchCard = Regex.Match(
            homeHtml,
            "<article(?=[^>]*data-game-id=\"Lantern\")[^>]*>");
        var nonMatchCard = Regex.Match(
            homeHtml,
            "<article(?=[^>]*data-game-id=\"Celeste\")[^>]*>");

        Assert.True(titleMatchCard.Success);
        Assert.True(noteMatchCard.Success);
        Assert.True(nonMatchCard.Success);
        Assert.DoesNotContain(" hidden", titleMatchCard.Value);
        Assert.DoesNotContain(" hidden", noteMatchCard.Value);
        Assert.Contains(" hidden", nonMatchCard.Value);
    }

    [Fact]
    public async Task Details_inline_controls_update_and_clear_frontmatter_without_changing_notes()
    {
        using var app = new TestApp();
        const string markdown = "## Notes\nKeep this text.";
        app.WriteGame("Inline Game", $"---\ntype: game\nstatus: active\nrating: 6\n---\n{markdown}");

        var detailsHtml = await app.Client.GetStringAsync("/Games/Details/Inline%20Game");
        Assert.Single(Regex.Matches(detailsHtml, "aria-label=\"Change status for Inline Game\""));
        Assert.Single(Regex.Matches(detailsHtml, "aria-label=\"Change rating for Inline Game\""));

        var ratingToken = TestHelpers.GetAntiforgeryToken(detailsHtml);
        var ratingResponse = await app.Client.PostAsync(
            "/Games/Details/Inline%20Game?handler=ChangeRating",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ratingToken,
                ["rating"] = "8.5"
            }));
        Assert.Equal(HttpStatusCode.OK, ratingResponse.StatusCode);

        detailsHtml = await app.Client.GetStringAsync("/Games/Details/Inline%20Game");
        var statusResponse = await app.Client.PostAsync(
            "/Games/Details/Inline%20Game?handler=ChangeStatus",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = TestHelpers.GetAntiforgeryToken(detailsHtml),
                ["status"] = "completed"
            }));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        detailsHtml = await app.Client.GetStringAsync("/Games/Details/Inline%20Game");
        var clearResponse = await app.Client.PostAsync(
            "/Games/Details/Inline%20Game?handler=ChangeRating",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = TestHelpers.GetAntiforgeryToken(detailsHtml),
                ["clear"] = "true"
            }));
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var savedNote = await File.ReadAllTextAsync(app.GamePath("Inline Game"));
        Assert.Contains("status: completed", savedNote);
        Assert.DoesNotContain("rating:", savedNote);
        Assert.Contains(markdown, savedNote);
    }

    [Fact]
    public async Task Details_page_prioritizes_tracker_overview_and_notes_before_media()
    {
        using var app = new TestApp();
        app.WriteGame(
            "Ordered Game",
            "---\ntype: game\nstatus: active\nrating: 7\nplatform: PC\n---\n## Notes\nKeep the journal prominent.");

        var detailsHtml = await app.Client.GetStringAsync("/Games/Details/Ordered%20Game");
        var trackerIndex = detailsHtml.IndexOf("class=\"game-details-strip\"", StringComparison.Ordinal);
        var overviewIndex = detailsHtml.IndexOf("class=\"game-description-card\"", StringComparison.Ordinal);
        var notesIndex = detailsHtml.IndexOf("class=\"note-card\"", StringComparison.Ordinal);
        var mediaIndex = detailsHtml.IndexOf("class=\"game-media-collection\"", StringComparison.Ordinal);

        Assert.True(trackerIndex >= 0);
        Assert.True(trackerIndex < overviewIndex);
        Assert.True(overviewIndex < notesIndex);
        Assert.True(notesIndex < mediaIndex);
        Assert.Contains("Game details", detailsHtml);
        Assert.Contains("platform", detailsHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Frontmatter", detailsHtml);
    }
}
