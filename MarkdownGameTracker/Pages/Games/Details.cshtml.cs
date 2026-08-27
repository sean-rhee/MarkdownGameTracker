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
        "type", "hobby", "status", "rating"
    };

    public GameNote Game { get; private set; } = null!;

    public string RenderedMarkdown { get; private set; } = string.Empty;

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

        Game = game;
        RenderedMarkdown = markdownRenderer.Render(game.Markdown);
        return Page();
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
