using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;

namespace MarkdownGameTracker.Services;

public interface IGameMetadataService
{
    Task<GameNote?> ChangeStatusAsync(
        string id,
        string status,
        CancellationToken cancellationToken);

    Task<GameNote?> SetRatingAsync(
        string id,
        decimal rating,
        CancellationToken cancellationToken);

    Task<GameNote?> ClearRatingAsync(
        string id,
        CancellationToken cancellationToken);
}

public sealed class GameMetadataService(IGameRepository repository) : IGameMetadataService
{
    public Task<GameNote?> ChangeStatusAsync(
        string id,
        string status,
        CancellationToken cancellationToken)
    {
        if (!GameStatuses.TryNormalize(status, out var normalizedStatus))
        {
            throw new ArgumentException("The status is not supported.", nameof(status));
        }

        return repository.UpdateAsync(
            id,
            new UpdateGameRequest(null, normalizedStatus, null, null, null),
            cancellationToken);
    }

    public Task<GameNote?> SetRatingAsync(
        string id,
        decimal rating,
        CancellationToken cancellationToken)
    {
        return repository.UpdateAsync(
            id,
            new UpdateGameRequest(null, null, rating, null, null),
            cancellationToken);
    }

    public Task<GameNote?> ClearRatingAsync(
        string id,
        CancellationToken cancellationToken)
    {
        return repository.UpdateAsync(
            id, new UpdateGameRequest(null, null, null, null, null, ClearRating: true), cancellationToken);
    }
}
