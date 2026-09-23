using MarkdownGameTracker.Models;

namespace MarkdownGameTracker.Storage;

public interface IGameRepository
{
    Task<GameLibraryScan> ScanAsync(string? status, string? search, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameNote>> ListAsync(
        string? status,
        string? search,
        CancellationToken cancellationToken);

    Task<GameNote?> GetAsync(string id, CancellationToken cancellationToken);

    Task<GameNote> CreateAsync(CreateGameRequest request, CancellationToken cancellationToken);

    Task<GameNote?> UpdateAsync(
        string id,
        UpdateGameRequest request,
        CancellationToken cancellationToken);

    Task<ProgressJournalUpdateResult> UpdateProgressJournalAsync(
        string id, string expectedVersion, string content, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}

public sealed class GameAlreadyExistsException(string title)
    : Exception($"A game note named '{title}' already exists.");

public sealed record GameLibraryScan(IReadOnlyList<GameNote> Games, IReadOnlyList<GameNoteDiagnostic> Diagnostics);

public sealed record GameNoteDiagnostic(string FileName, string Message);

public sealed record ProgressJournalUpdateResult(bool Saved, bool Conflict, GameNote? Game, string? Error = null)
{
    public static ProgressJournalUpdateResult NotFound() => new(false, false, null);
    public static ProgressJournalUpdateResult Changed(GameNote game) => new(false, true, game);
    public static ProgressJournalUpdateResult Updated(GameNote game) => new(true, false, game);
}
