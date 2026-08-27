using System.ComponentModel;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;

namespace MarkdownGameTracker.Api;

public static class GameEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var games = endpoints.MapGroup("/api/games")
            .WithTags("Games");

        games.MapGet("/", ListGamesAsync)
            .WithName("ListGames")
            .WithSummary("List game notes")
            .WithDescription("Lists game notes, optionally filtered by status or searched by title and Markdown content.")
            .Produces<IReadOnlyList<GameNote>>(StatusCodes.Status200OK);
        games.MapGet("/{id}", GetGameAsync)
            .WithName("GetGame")
            .WithSummary("Get a game note")
            .WithDescription("Gets a game note by its filename without the .md extension. URL-encode spaces and punctuation in the ID.")
            .Produces<GameNote>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
        games.MapPost("/", CreateGameAsync)
            .WithName("CreateGame")
            .WithSummary("Create a game note")
            .WithDescription("Creates a Markdown note with YAML frontmatter in the configured Games directory.")
            .Accepts<CreateGameRequest>("application/json")
            .Produces<GameNote>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
        games.MapPut("/{id}", UpdateGameAsync)
            .WithName("UpdateGame")
            .WithSummary("Update a game note")
            .WithDescription("Merges supplied values into an existing note. Changing title renames the Markdown file; null frontmatter values remove those keys.")
            .Accepts<UpdateGameRequest>("application/json")
            .Produces<GameNote>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
        games.MapDelete("/{id}", DeleteGameAsync)
            .WithName("DeleteGame")
            .WithSummary("Delete a game note")
            .WithDescription("Permanently deletes a game note from the configured Games directory.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> ListGamesAsync(
        [Description("Exact status to match: active, endless, completed, inactive, or planned.")]
        string? status,
        [Description("Case-insensitive text to find in a game title or Markdown body.")]
        string? search,
        IGameRepository repository,
        CancellationToken cancellationToken)
    {
        var results = await repository.ListAsync(status, search, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetGameAsync(
        [Description("Game-note filename without the .md extension.")]
        string id,
        IGameRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var game = await repository.GetAsync(id, cancellationToken);
            return game is null
                ? Problem(StatusCodes.Status404NotFound, "Game not found", $"No game note named '{id}' exists.")
                : Results.Ok(game);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> CreateGameAsync(
        CreateGameRequest request,
        IGameRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var game = await repository.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/games/{Uri.EscapeDataString(game.Id)}", game);
        }
        catch (GameAlreadyExistsException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "Game already exists", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> UpdateGameAsync(
        [Description("Game-note filename without the .md extension.")]
        string id,
        UpdateGameRequest request,
        IGameRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var game = await repository.UpdateAsync(id, request, cancellationToken);
            return game is null
                ? Problem(StatusCodes.Status404NotFound, "Game not found", $"No game note named '{id}' exists.")
                : Results.Ok(game);
        }
        catch (GameAlreadyExistsException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "Game already exists", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> DeleteGameAsync(
        [Description("Game-note filename without the .md extension.")]
        string id,
        IGameRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            return await repository.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Problem(StatusCodes.Status404NotFound, "Game not found", $"No game note named '{id}' exists.");
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static IResult InvalidRequest(ArgumentException exception)
    {
        var field = string.IsNullOrWhiteSpace(exception.ParamName)
            ? "request"
            : exception.ParamName;

        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [field] = [exception.Message]
            });
    }

    private static IResult Problem(int statusCode, string title, string detail) =>
        Results.Problem(statusCode: statusCode, title: title, detail: detail);
}
