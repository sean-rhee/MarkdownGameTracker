using System.Text.Json;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages.Games;

public sealed class DetailsModel(
    IGameRepository repository,
    IGameMetadataService metadataService,
    MarkdownRenderer markdownRenderer) : PageModel
{
    private static readonly HashSet<string> PrimaryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "hobby", "title", "aliases", "source", "hltb_added", "hltb_updated", "status", "rating"
    };

    public GameNote Game { get; private set; } = null!;

    public string? SelectionSourceId { get; private set; }
    public string? SelectedIgdbGameId { get; private set; }

    public string RenderedMarkdown { get; private set; } = string.Empty;

    public string RenderedProgressJournal { get; private set; } = string.Empty;

    public ProgressJournalSection ProgressJournal { get; private set; } = null!;

    [BindProperty]
    public string? JournalDraft { get; set; }

    [BindProperty]
    public string? JournalVersion { get; set; }

    public string? JournalError { get; private set; }

    public IReadOnlyDictionary<string, object?> ExtraFrontmatter => Game.Frontmatter
        .Where(pair => !PrimaryFields.Contains(pair.Key))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var game = await repository.GetAsync(id, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        LoadGame(game);
        if (string.Equals(TempData.Peek("IgdbSelectionTarget") as string, game.Id, StringComparison.Ordinal))
        {
            TempData.Remove("IgdbSelectionTarget");
            SelectionSourceId = TempData["IgdbSelectionSource"] as string;
            SelectedIgdbGameId = TempData["IgdbSelectedId"] as string;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveProgressAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(JournalVersion))
        {
            return BadRequest();
        }

        var game = await repository.GetAsync(id, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        if (ProgressJournalDocument.ContainsAnotherMajorHeading(JournalDraft ?? string.Empty))
        {
            LoadGame(game);
            JournalError = "Use ### for headings inside the journal. Edit the full note to add another main section.";
            return Page();
        }

        var update = await repository.UpdateProgressJournalAsync(
            id, JournalVersion, JournalDraft ?? string.Empty, cancellationToken);
        if (update.Game is null)
        {
            return NotFound();
        }

        if (update.Error is not null)
        {
            LoadGame(update.Game);
            JournalError = update.Error;
            return Page();
        }

        if (update.Conflict)
        {
            LoadGame(update.Game);
            JournalVersion = ProgressJournal.Version;
            ModelState.Remove(nameof(JournalVersion));
            JournalError = "The journal changed while you were editing. Your draft is still here. Compare it with the current journal below, then save again if you want to replace it.";
            return Page();
        }

        TempData["SuccessMessage"] = "Progress journal saved.";
        return RedirectToPage("/Games/Details", null, new { id = update.Game.Id }, "progress-journal");
    }

    private void LoadGame(GameNote game)
    {
        Game = game;
        ProgressJournal = ProgressJournalDocument.Read(game.Markdown);
        (RenderedProgressJournal, RenderedMarkdown) = markdownRenderer.RenderSections(game.Markdown, ProgressJournal);
        JournalDraft ??= ProgressJournal.Content;
        JournalVersion ??= ProgressJournal.Version;
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (!await repository.DeleteAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = $"Deleted {id}.";
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostChangeStatusAsync(
        string id,
        string status,
        CancellationToken cancellationToken)
    {
        if (!GameStatuses.TryNormalize(status, out var normalizedStatus))
        {
            return BadRequest();
        }

        var game = await metadataService.ChangeStatusAsync(id, normalizedStatus, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        SuccessMessage = $"Changed the status to {GameStatuses.GetLabel(normalizedStatus)}.";
        return RedirectToPage(new { id = game.Id });
    }

    public async Task<IActionResult> OnPostChangeRatingAsync(
        string id,
        decimal? rating,
        bool clear,
        CancellationToken cancellationToken)
    {
        if (!clear && (rating is null or < 0 or > 10))
        {
            return BadRequest();
        }

        var game = clear
            ? await metadataService.ClearRatingAsync(id, cancellationToken)
            : await metadataService.SetRatingAsync(id, rating!.Value, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        SuccessMessage = clear
            ? "Cleared the rating."
            : $"Changed the rating to {game.Rating} out of 10.";
        return RedirectToPage(new { id = game.Id });
    }

    public static string FormatFrontmatterValue(object? value)
    {
        return value switch
        {
            null => "—",
            string text => text,
            _ => JsonSerializer.Serialize(value)
        };
    }
}
