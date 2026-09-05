namespace MarkdownGameTracker.Models;

public sealed record InlineStatusControlModel(
    string GameId,
    string? Status,
    string Page,
    string? ReturnStatus = null,
    string? Search = null,
    string? Sort = null);

public sealed record InlineRatingControlModel(
    string GameId,
    decimal? Rating,
    string Page,
    string? ReturnStatus = null,
    string? Search = null,
    string? Sort = null);
