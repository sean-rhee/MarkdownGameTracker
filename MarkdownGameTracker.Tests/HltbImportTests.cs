using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using Xunit;

namespace MarkdownGameTracker.Tests;

public sealed class HltbImportTests
{
    [Fact]
    public async Task Invalid_filenames_are_reported_per_row_and_valid_rows_can_be_imported()
    {
        using var app = new TestApp();
        var csv = "Title,Platform,Playing,Backlog,Endless,Dropped,Completed,Retired\n"
                  + "Good,PC,X,,,,,\n"
                  + "..,PC,X,,,,,\n"
                  + new string('x', 201) + ",PC,X,,,,,\n"
                  + "Shared,..,X,,,,,\n"
                  + "Shared,PC,X,,,,,\n";
        var page = await app.Client.GetStringAsync("/Games/Import");
        using var previewResponse = await PostCsvAsync(app, page, csv);
        var preview = await previewResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Contains("2</strong> ready", preview);
        Assert.Contains("3</strong> invalid", preview);
        Assert.Contains("Invalid note filename", preview);
        Assert.Empty(Directory.GetFiles(Path.Combine(app.RootPath, "Games")));

        var token = Regex.Match(preview, "name=\"importToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        using var response = await app.Client.PostAsync("/Games/Import?handler=Import",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = TestHelpers.GetAntiforgeryToken(preview),
                ["importToken"] = WebUtility.HtmlDecode(token)
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Created 2 notes", await response.Content.ReadAsStringAsync());
        Assert.True(File.Exists(app.GamePath("Good")));
        Assert.True(File.Exists(app.GamePath("Shared (PC)")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(app.RootPath, "Games")).Length);
    }

    private const string Csv = """
        "Title","Platform","Playing","Backlog","Endless","Dropped","Completed","Retired","Start Date","Completion Date","General Notes","Review Notes","Added","Updated"
        "Shared: Game","PC","","","","","X","","","2026-07-05","Finished it.","","2026-08-01 10:00:00","2026-08-01 10:00:00"
        "Shared: Game","Nintendo Switch","","X","","","","","","","","","2026-08-01 10:00:01","2026-08-01 10:00:01"
        "Shared: Game","PC","","","","","","X","","","","","2026-08-01 10:00:02","2026-08-01 10:00:02"
        "Existing","PC","X","","","","","","","","","","2026-08-01 10:00:03","2026-08-01 10:00:03"
        "Bad Date","PC","","","","","X","","","2026-00-00","","","2026-08-01 10:00:04","2026-08-01 10:00:04"
        "No Status","PC","","","","","","","","","","","2026-08-01 10:00:05","2026-08-01 10:00:05"
        """;

    [Fact]
    public async Task Import_preview_uses_platform_collisions_and_imports_only_ready_rows()
    {
        using var app = new TestApp();
        app.WriteGame("Existing", "---\ntype: game\n---\nKeep this note.");

        var getHtml = await app.Client.GetStringAsync("/Games/Import");
        var previewResponse = await PostCsvAsync(app, getHtml);
        var previewHtml = await previewResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Contains("4</strong> ready", previewHtml);
        Assert.Contains("1</strong> existing", previewHtml);
        Assert.Contains("1</strong> invalid", previewHtml);
        Assert.Contains("Shared - Game (PC).md", previewHtml);
        Assert.Contains("Shared - Game (Nintendo Switch).md", previewHtml);
        Assert.Contains("Shared - Game (PC, 2).md", previewHtml);
        Assert.Contains("Ignored invalid completion date", previewHtml);
        Assert.Contains(">Inactive<", previewHtml);

        var importToken = WebUtility.HtmlDecode(Regex.Match(
            previewHtml,
            "name=\"importToken\" value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(importToken);
        var antiforgeryToken = TestHelpers.GetAntiforgeryToken(previewHtml);
        var importResponse = await app.Client.PostAsync(
            "/Games/Import?handler=Import",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["importToken"] = importToken
            }));
        var importHtml = await importResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.Contains("Created 4 notes", importHtml);
        Assert.True(File.Exists(app.GamePath("Shared - Game (PC)")));
        Assert.True(File.Exists(app.GamePath("Shared - Game (Nintendo Switch)")));
        Assert.True(File.Exists(app.GamePath("Shared - Game (PC, 2)")));
        Assert.True(File.Exists(app.GamePath("Bad Date")));
        Assert.Equal("---\ntype: game\n---\nKeep this note.", await File.ReadAllTextAsync(app.GamePath("Existing")));

        var imported = await File.ReadAllTextAsync(app.GamePath("Shared - Game (PC)"));
        Assert.Contains("title: 'Shared: Game'", imported);
        Assert.Contains("platform: PC", imported);
        Assert.Contains("status: completed", imported);
        Assert.Contains("completion_date: 2026-07-05", imported);
        Assert.Contains("Finished it.", imported);
    }

    [Fact]
    public async Task Import_page_is_linked_and_an_expired_preview_cannot_write()
    {
        using var app = new TestApp();

        var homeHtml = await app.Client.GetStringAsync("/");
        var importHtml = await app.Client.GetStringAsync("/Games/Import");
        var antiforgeryToken = TestHelpers.GetAntiforgeryToken(importHtml);
        var response = await app.Client.PostAsync(
            "/Games/Import?handler=Import",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["importToken"] = "expired"
            }));

        Assert.Contains("Import HLTB", homeHtml);
        Assert.Contains("preview expired", await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostCsvAsync(TestApp app, string pageHtml, string csv = Csv)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(TestHelpers.GetAntiforgeryToken(pageHtml)), "__RequestVerificationToken");
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "CsvFile", "HLTB.csv");
        return await app.Client.PostAsync("/Games/Import?handler=Preview", content);
    }
}
