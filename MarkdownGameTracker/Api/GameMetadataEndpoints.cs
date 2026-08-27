using System.ComponentModel;
using MarkdownGameTracker.Services;
using MarkdownGameTracker.Storage;

namespace MarkdownGameTracker.Api;

public static class GameMetadataEndpoints
{
    public static IEndpointRouteBuilder MapGameMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/games/{id}/igdb-description",
                async (
                    [Description("Game-note filename without the .md extension.")] string id,
                    [Description("Optional IGDB game ID chosen from the matches endpoint.")] long? igdbId,
                    IGameRepository repository,
                    IIgdbDescriptionService igdb,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        if (igdbId is <= 0)
                        {
                            return Results.ValidationProblem(new Dictionary<string, string[]>
                            {
                                ["igdbId"] = ["IGDB game ID must be greater than zero."]
                            });
                        }

                        var game = await repository.GetAsync(id, cancellationToken);
                        if (game is null)
                        {
                            return Results.Problem(
                                statusCode: StatusCodes.Status404NotFound,
                                title: "Game not found",
                                detail: $"No game note named '{id}' exists.");
                        }

                        var result = await igdb.GetDescriptionAsync(game.Title, igdbId, cancellationToken);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException exception)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [exception.ParamName ?? "id"] = [exception.Message]
                        });
                    }
                })
            .WithTags("Game Metadata")
            .WithName("GetIgdbGameDescription")
            .WithSummary("Get a game's IGDB description")
            .WithDescription("Looks up the game note's title on IGDB, or loads a chosen IGDB game ID, without modifying its Markdown or YAML frontmatter.")
            .Produces<IgdbDescriptionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        endpoints.MapGet(
                "/api/games/{id}/igdb-matches",
                async (
                    [Description("Game-note filename without the .md extension.")] string id,
                    IGameRepository repository,
                    IIgdbDescriptionService igdb,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var game = await repository.GetAsync(id, cancellationToken);
                        if (game is null)
                        {
                            return Results.Problem(
                                statusCode: StatusCodes.Status404NotFound,
                                title: "Game not found",
                                detail: $"No game note named '{id}' exists.");
                        }

                        var result = await igdb.SearchMatchesAsync(game.Title, cancellationToken);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException exception)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [exception.ParamName ?? "id"] = [exception.Message]
                        });
                    }
                })
            .WithTags("Game Metadata")
            .WithName("SearchIgdbGameMatches")
            .WithSummary("Find possible IGDB matches for a game")
            .WithDescription("Returns likely IGDB titles and release years without modifying the game note.")
            .Produces<IgdbMatchSearchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        endpoints.MapPost(
                "/api/games/igdb-card-artwork",
                async (
                    GameArtworkBatchRequest request,
                    IGameRepository repository,
                    IIgdbDescriptionService igdb,
                    CancellationToken cancellationToken) =>
                {
                    var selections = request.Games?
                        .Where(game => !string.IsNullOrWhiteSpace(game.Id))
                        .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
                        .ToArray() ?? [];
                    if (selections.Length == 0 || selections.Length > 100)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["games"] = ["Provide between 1 and 100 game selections."]
                        });
                    }

                    if (selections.Any(game => game.IgdbGameId is <= 0))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["igdbGameId"] = ["IGDB game IDs must be greater than zero."]
                        });
                    }

                    var gameNotes = await repository.ListAsync(null, null, cancellationToken);
                    var notesById = gameNotes.ToDictionary(game => game.Id, StringComparer.OrdinalIgnoreCase);
                    var lookups = selections
                        .Where(selection => notesById.ContainsKey(selection.Id))
                        .Select(selection =>
                        {
                            var game = notesById[selection.Id];
                            return new IgdbArtworkLookup(game.Id, game.Title, selection.IgdbGameId);
                        })
                        .ToArray();
                    var result = await igdb.GetCardArtworkAsync(lookups, cancellationToken);
                    return Results.Ok(result);
                })
            .WithTags("Game Metadata")
            .WithName("GetIgdbCardArtwork")
            .WithSummary("Get IGDB artwork for game cards")
            .WithDescription("Batch-loads cached IGDB artwork for game-note cards. Optional IGDB IDs take priority over title matching, and notes are not modified.")
            .Accepts<GameArtworkBatchRequest>("application/json")
            .Produces<IgdbCardArtworkResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return endpoints;
    }
}

public sealed record GameArtworkBatchRequest(IReadOnlyList<GameArtworkSelection>? Games);

public sealed record GameArtworkSelection(string Id, long? IgdbGameId);
