using System.Text.Json;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages.Games;

public sealed class DetailsModel(IGameRepository repository, MarkdownRenderer markdownRenderer) : PageModel
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
