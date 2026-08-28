using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using MarkdownGameTracker.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MarkdownGameTracker.Pages;

public sealed class IndexModel(
    IGameRepository repository,
    IGameMetadataService metadataService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public string SelectedStatus { get; private set; } = GameStatuses.Active;

    public IReadOnlyList<GameNote> Games { get; private set; } = [];

    public int VisibleGameCount { get; private set; }

    public IReadOnlyList<string> SearchSuggestions { get; private set; } = [];

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
        Games = allGames
            .Where(game => string.Equals(
                game.Status,
                SelectedStatus,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        VisibleGameCount = Games.Count(IsSearchMatch);
        SearchSuggestions = Games
            .Select(game => game.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    public bool IsSearchMatch(GameNote game) => MatchesSearch(game, Search);

    public static string GetSearchText(GameNote game) =>
        $"{game.Title}\n{GetPlatform(game)}\n{game.Markdown}";

    public static string? GetPlatform(GameNote game) =>
        GameNoteMetadata.GetString(game, GameNoteMetadata.PlatformKey);

    private static bool MatchesSearch(GameNote game, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        return game.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
               || game.Markdown.Contains(term, StringComparison.OrdinalIgnoreCase);
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

        var game = await metadataService.ChangeStatusAsync(id, normalizedStatus, cancellationToken);

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

    public async Task<IActionResult> OnPostChangeRatingAsync(
        string id,
        decimal? rating,
        bool clear,
        string? currentStatus,
        string? search,
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
            ? $"Cleared the rating for {game.Title}."
            : $"Rated {game.Title} {game.Rating} out of 10.";
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
