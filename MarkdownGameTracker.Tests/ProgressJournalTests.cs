using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Xunit;

namespace MarkdownGameTracker.Tests;

public sealed class ProgressJournalTests
{
    [Fact]
    public void Editing_journal_preserves_review_other_notes_and_fenced_heading_text()
    {
        var markdown = """
            Opening thought.

            ```markdown
            ## Progress Journal
            This is an example, not a section.
            ```

            ## Progress Journal

            Reached the village.
            ### Next objective
            Find the key.

            ## Review

            A detailed review.
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = ProgressJournalDocument.Read(markdown);
        Assert.True(section.Exists);
        Assert.False(section.HasDuplicateHeadings);
        Assert.StartsWith("Reached the village.", section.Content);
        Assert.Contains("## Review", section.OtherMarkdown);
        Assert.Single(Regex.Matches(section.OtherMarkdown, "## Progress Journal").Cast<Match>());

        Assert.True(ProgressJournalDocument.TryReplace(markdown, section.Version,
            "Returned to the village.\n### Next objective\nOpen the gate.", out var updated));
        Assert.StartsWith(markdown[..markdown.IndexOf("## Progress Journal\n\nReached", StringComparison.Ordinal)], updated);
        Assert.EndsWith(markdown[markdown.IndexOf("## Review", StringComparison.Ordinal)..], updated);
        Assert.Contains("Returned to the village.", updated);
        Assert.DoesNotContain("Reached the village.\n### Next objective", updated);
    }

    [Fact]
    public void Missing_journal_is_created_without_changing_review_and_duplicates_block_scoped_edits()
    {
        const string review = "## Review\nA long review.\n";
        var missing = ProgressJournalDocument.Read(review);
        Assert.False(missing.Exists);
        Assert.True(ProgressJournalDocument.TryReplace(review, missing.Version, "First session.", out var updated));
        Assert.StartsWith(review, updated);
        Assert.EndsWith("## Progress Journal\nFirst session.\n", updated);
        Assert.Equal("First session.", ProgressJournalDocument.Read(updated).Content);

        const string duplicate = "## Progress Journal\nFirst\n\n## Review\nKeep\n\n## Progress Journal\nSecond";
        Assert.True(ProgressJournalDocument.Read(duplicate).HasDuplicateHeadings);
        Assert.False(ProgressJournalDocument.TryReplace(duplicate,
            ProgressJournalDocument.Read(duplicate).Version, "Replacement", out var unchanged));
        Assert.Equal(duplicate, unchanged);
        Assert.True(ProgressJournalDocument.ContainsAnotherMajorHeading("A note\n\n## Review\nWrong place"));
        Assert.False(ProgressJournalDocument.ContainsAnotherMajorHeading("### Session 2\nJournal entry"));
    }

    [Fact]
    public async Task Section_save_merges_other_changes_and_requires_review_after_a_journal_conflict()
    {
        using var app = new TestApp();
        const string original = "---\ntype: game\nstatus: active\ncustom: keep\n---\n## Progress Journal\nStarted.\n\n## Review\nOriginal review.\n";
        app.WriteGame("Journal Game", original);
        var page = await app.Client.GetStringAsync("/Games/Details/Journal%20Game");
        Assert.True(page.IndexOf("Progress Journal</h2>", StringComparison.Ordinal)
            < page.IndexOf("About the game</h2>", StringComparison.Ordinal));
        var otherNotes = page[page.IndexOf("<article class=\"note-card\"", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Started.", otherNotes);
        Assert.Contains("Original review.", page);

        var token = TestHelpers.GetAntiforgeryToken(page);
        var firstVersion = ProgressJournalDocument.Read("## Progress Journal\nStarted.\n\n## Review\nOriginal review.\n").Version;
        using var otherSectionChange = await app.Client.PutAsJsonAsync("/api/games/Journal%20Game",
            new { markdown = "## Progress Journal\nStarted.\n\n## Review\nRevised elsewhere.\n" });
        Assert.Equal(HttpStatusCode.OK, otherSectionChange.StatusCode);

        var saved = await PostJournal(app, "Journal Game", token, firstVersion, "Reached chapter two.");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var afterSave = await File.ReadAllTextAsync(app.GamePath("Journal Game"));
        Assert.Contains("Reached chapter two.", afterSave);
        Assert.Contains("Revised elsewhere.", afterSave);
        Assert.Contains("custom: keep", afterSave);

        var current = await app.Client.GetFromJsonAsync<GameNote>("/api/games/Journal%20Game");
        var oldVersion = ProgressJournalDocument.Read(current!.Markdown).Version;
        using var changedJournal = await app.Client.PutAsJsonAsync("/api/games/Journal%20Game",
            new { markdown = "## Progress Journal\nChanged in Obsidian.\n\n## Review\nRevised elsewhere.\n" });
        Assert.Equal(HttpStatusCode.OK, changedJournal.StatusCode);

        var conflict = await PostJournal(app, "Journal Game", token, oldVersion, "My unsaved draft.");
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("changed while you were editing", conflictHtml);
        Assert.Contains("My unsaved draft.", conflictHtml);
        Assert.Contains("Changed in Obsidian.", conflictHtml);
        Assert.DoesNotContain("My unsaved draft.", await File.ReadAllTextAsync(app.GamePath("Journal Game")));

        var renewedVersion = ProgressJournalDocument.Read("## Progress Journal\nChanged in Obsidian.\n\n## Review\nRevised elsewhere.\n").Version;
        Assert.Contains($"value=\"{renewedVersion}\"", conflictHtml);
        var retry = await PostJournal(app, "Journal Game", token, renewedVersion, "My unsaved draft.");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var afterRetry = await File.ReadAllTextAsync(app.GamePath("Journal Game"));
        Assert.Contains("My unsaved draft.", afterRetry);
        Assert.Contains("Revised elsewhere.", afterRetry);
    }

    [Fact]
    public async Task Missing_journal_can_be_started_from_details_page()
    {
        using var app = new TestApp();
        app.WriteGame("New Journal", "---\ntype: game\nstatus: active\n---\n## Review\nKeep this review.\n");
        var page = await app.Client.GetStringAsync("/Games/Details/New%20Journal");
        Assert.Contains("Start a progress journal", page);
        Assert.Contains("No progress recorded yet.", page);
        var version = ProgressJournalDocument.Read("## Review\nKeep this review.\n").Version;
        var saved = await PostJournal(app, "New Journal", TestHelpers.GetAntiforgeryToken(page), version, "First session.");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var markdown = (await app.Client.GetFromJsonAsync<GameNote>("/api/games/New%20Journal"))!.Markdown;
        Assert.Equal("First session.", ProgressJournalDocument.Read(markdown).Content);
        Assert.Contains("## Review\nKeep this review.", markdown);
    }

    [Theory]
    [InlineData("```text\nUnfinished")]
    [InlineData("~~~\nUnfinished")]
    [InlineData("<!-- unfinished")]
    public void Edits_cannot_absorb_following_sections(string draft)
    {
        const string original = "## Progress Journal\nStarted.\n\n## Review\nKeep this.\n";
        Assert.False(ProgressJournalDocument.TryReplace(original,
            ProgressJournalDocument.Read(original).Version, draft, out var result, out var error));
        Assert.Equal(original, result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Missing_journal_cannot_be_appended_inside_an_unfinished_block()
    {
        const string original = "## Review\n```\nUnfinished";
        Assert.False(ProgressJournalDocument.TryReplace(original,
            ProgressJournalDocument.Read(original).Version, "New entry", out var result));
        Assert.Equal(original, result);
    }

    [Fact]
    public void Sections_share_reference_definitions_in_both_directions()
    {
        const string markdown = "## Progress Journal\n[Guide][g]\n\n[r]: https://example.com/review\n\n## Review\n[Review][r]\n\n[g]: https://example.com/guide\n";
        var renderer = new MarkdownGameTracker.Services.MarkdownRenderer();
        var (journal, other) = renderer.RenderSections(markdown, ProgressJournalDocument.Read(markdown));
        Assert.Contains("href=\"https://example.com/guide\"", journal);
        Assert.Contains("href=\"https://example.com/review\"", other);
        Assert.DoesNotContain("<h2", journal);
        Assert.DoesNotContain(">Guide</a>", other);
    }

    [Fact]
    public async Task Invalid_boundaries_and_duplicate_conflicts_preserve_draft_and_disk()
    {
        using var app = new TestApp();
        const string body = "## Progress Journal\nStarted.\n\n## Review\nKeep this.\n";
        const string original = "---\ntype: game\nstatus: active\n---\n" + body;
        app.WriteGame("Boundary Game", original);
        var page = await app.Client.GetStringAsync("/Games/Details/Boundary%20Game");
        var token = TestHelpers.GetAntiforgeryToken(page);
        var version = ProgressJournalDocument.Read(body).Version;
        using var invalid = await PostJournal(app, "Boundary Game", token, version, "```\nUnsaved draft");
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("section boundaries", invalidHtml);
        Assert.Contains("Unsaved draft", invalidHtml);
        Assert.Equal(original, await File.ReadAllTextAsync(app.GamePath("Boundary Game")));

        var duplicate = original + "\n## Progress Journal\nAdded elsewhere.\n";
        await File.WriteAllTextAsync(app.GamePath("Boundary Game"), duplicate);
        using var conflict = await PostJournal(app, "Boundary Game", token, version, "My recoverable draft");
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Matches("(?s)<textarea[^>]*readonly[^>]*>.*?My recoverable draft.*?</textarea>", conflictHtml);
        Assert.Contains("Your draft was not saved", conflictHtml);
        Assert.Equal(duplicate, await File.ReadAllTextAsync(app.GamePath("Boundary Game")));
    }

    private static Task<HttpResponseMessage> PostJournal(TestApp app, string id, string token,
        string version, string content) => app.Client.PostAsync(
            $"/Games/Details/{Uri.EscapeDataString(id)}?handler=SaveProgress",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["JournalVersion"] = version,
                ["JournalDraft"] = content
            }));
}
