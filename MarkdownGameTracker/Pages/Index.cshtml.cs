using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages;

public sealed class IndexModel(IGameRepository repository) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public string SelectedStatus { get; private set; } = GameStatuses.Active;

    public IReadOnlyList<GameNote> Games { get; private set; } = [];

    public IReadOnlyList<StatusTab> StatusTabs { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SelectedStatus = GameStatuses.TryNormalize(Status, out var normalizedStatus)
            ? normalizedStatus
            : GameStatuses.Active;
        Status = SelectedStatus;

        var allGames = await repository.ListAsync(null, null, cancellationToken);
        Games = await repository.ListAsync(SelectedStatus, Search, cancellationToken);
        StatusTabs = GameStatuses.All
            .Select(status => new StatusTab(
                status,
                GameStatuses.GetLabel(status),
                allGames.Count(game => string.Equals(
                    game.Status,
                    status,
                    StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    public async Task<IActionResult> OnPostChangeStatusAsync(
        string id,
        string status,
        string? currentStatus,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!GameStatuses.TryNormalize(status, out var normalizedStatus))
        {
            return BadRequest();
        }

        var game = await repository.UpdateAsync(
            id,
            new UpdateGameRequest(null, normalizedStatus, null, null, null),
            cancellationToken);

        if (game is null)
        {
            return NotFound();
        }

        SuccessMessage = $"Moved {game.Title} to {GameStatuses.GetLabel(normalizedStatus)}.";
        var returnStatus = GameStatuses.TryNormalize(currentStatus, out var normalizedCurrentStatus)
            ? normalizedCurrentStatus
            : GameStatuses.Active;

        return RedirectToPage(new
        {
            status = returnStatus,
            search
        });
    }

    public static string GetExcerpt(string markdown)
    {
        var text = string.Join(
            " ",
            markdown
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.TrimStart('#', '-', '*', ' '))
                .Where(line => !string.IsNullOrWhiteSpace(line)));

        return text.Length switch
        {
            0 => "No notes yet. Open the game to add your thoughts.",
            <= 150 => text,
            _ => $"{text[..147]}…"
        };
    }

    public static string GetStatusClass(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            GameStatuses.Active => "status-active",
            GameStatuses.Endless => "status-endless",
            GameStatuses.Completed => "status-completed",
            GameStatuses.Planned => "status-planned",
            GameStatuses.Inactive => "status-inactive",
            _ => "status-default"
        };
    }

    public sealed record StatusTab(string Value, string Label, int Count);
}
