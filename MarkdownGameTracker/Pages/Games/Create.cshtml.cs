using System.ComponentModel.DataAnnotations;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages.Games;

public sealed class CreateModel(IGameRepository repository) : PageModel
{
    [BindProperty]
    public GameFormInput Input { get; set; } = new();

    [BindProperty, Range(typeof(long), "1", "9223372036854775807")]
    public long? SelectedIgdbGameId { get; set; }

    [BindProperty, StringLength(200)]
    public string? SelectedIgdbTitle { get; set; }

    public void OnGet()
    {
        Input.Status = GameStatuses.Planned;
        Input.Markdown = "## Thoughts\n";
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var game = await repository.CreateAsync(
                new CreateGameRequest(
                    Input.Title,
                    Input.Status,
                    Input.Rating,
                    Input.Markdown,
                    Input.ToFrontmatter()),
                cancellationToken);

            if (SelectedIgdbGameId is > 0 && string.Equals(SelectedIgdbTitle, game.Title, StringComparison.Ordinal))
            {
                TempData["IgdbSelectionTarget"] = game.Id;
                TempData["IgdbSelectedId"] = SelectedIgdbGameId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            TempData["SuccessMessage"] = $"Added {game.Title} to your game garden.";
            return RedirectToPage("/Games/Details", new { id = game.Id });
        }
        catch (GameAlreadyExistsException exception)
        {
            ModelState.AddModelError("Input.Title", exception.Message);
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return Page();
    }
}
