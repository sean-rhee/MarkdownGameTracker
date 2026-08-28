using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages.Games;

public sealed class EditModel(IGameRepository repository) : PageModel
{
    [BindProperty]
    public GameFormInput Input { get; set; } = new();

    public string OriginalId { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var game = await repository.GetAsync(id, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        OriginalId = game.Id;
        Input = new GameFormInput
        {
            Title = game.Title,
            Status = game.Status ?? GameStatuses.Planned,
            Rating = game.Rating,
            Platform = GameNoteMetadata.GetString(game, GameNoteMetadata.PlatformKey),
            StartDate = GameNoteMetadata.GetDate(game, GameNoteMetadata.StartDateKey),
            CompletionDate = GameNoteMetadata.GetDate(game, GameNoteMetadata.CompletionDateKey),
            Markdown = game.Markdown
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        OriginalId = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var fieldsToClear = Input.ToFrontmatter();
        if (Input.Rating is null)
        {
            fieldsToClear["rating"] = null;
        }

        try
        {
            var game = await repository.UpdateAsync(
                id,
                new UpdateGameRequest(
                    Input.Title,
                    Input.Status,
                    Input.Rating,
                    Input.Markdown,
                    fieldsToClear),
                cancellationToken);

            if (game is null)
            {
                return NotFound();
            }

            TempData["SuccessMessage"] = $"Saved changes to {game.Title}.";
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
