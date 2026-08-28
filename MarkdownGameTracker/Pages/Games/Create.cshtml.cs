using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages.Games;

public sealed class CreateModel(IGameRepository repository) : PageModel
{
    [BindProperty]
    public GameFormInput Input { get; set; } = new();

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
