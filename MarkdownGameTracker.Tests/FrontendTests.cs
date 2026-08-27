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
        Assert.Contains("data-game-artwork-grid", homeHtml);
        Assert.Contains("data-game-id=\"Frontend Game\"", homeHtml);
        Assert.Contains("game-card-artwork.js", homeHtml);
        Assert.Contains("aria-label=\"Change rating for Frontend Game\"", homeHtml);
        Assert.Contains("#b3d9ff", homeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h2", detailsHtml);
        Assert.Contains("data-game-description", detailsHtml);
        Assert.Contains("data-game-media", detailsHtml);
        Assert.Contains("game-hero-title", detailsHtml);
        Assert.Contains("data-has-hero=\"false\"", detailsHtml);
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
    public async Task Details_inline_controls_update_and_clear_frontmatter_without_changing_notes()
    {
        using var app = new TestApp();
        const string markdown = "## Notes\nKeep this text.";
        app.WriteGame("Inline Game", $"---\ntype: game\nstatus: active\nrating: 6\n---\n{markdown}");

        var detailsHtml = await app.Client.GetStringAsync("/Games/Details/Inline%20Game");
        Assert.Equal(2, Regex.Matches(detailsHtml, "aria-label=\"Change status for Inline Game\"").Count);
        Assert.Equal(2, Regex.Matches(detailsHtml, "aria-label=\"Change rating for Inline Game\"").Count);

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
    public async Task Details_styles_only_apply_row_layout_to_direct_frontmatter_rows()
    {
        using var app = new TestApp();

        var detailsCss = await app.Client.GetStringAsync("/css/game-details.css");
        var libraryCss = await app.Client.GetStringAsync("/css/game-library.css");
        var darkThemeCss = await app.Client.GetStringAsync("/css/theme-dark.css");

        Assert.Contains(".metadata-card dl > div", detailsCss);
        Assert.DoesNotContain(".metadata-card dl div {", detailsCss);
        Assert.Contains(".metadata-card dl > div", darkThemeCss);
        Assert.DoesNotContain(".metadata-card dl div {", darkThemeCss);
        Assert.Contains(".inline-metadata-control:has(> .dropdown-menu.show)", libraryCss);
    }
}
