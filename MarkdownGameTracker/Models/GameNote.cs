using System.ComponentModel.DataAnnotations;

namespace MarkdownGameTracker.Models;

public sealed record GameNote(
    string Id,
    string Title,
    string? Status,
    decimal? Rating,
    string Markdown,
    IReadOnlyDictionary<string, object?> Frontmatter,
    DateTimeOffset LastModifiedUtc);

public sealed record CreateGameRequest(
    [property: Required, StringLength(200)] string? Title,
    [property: Required] string? Status,
    [property: Range(typeof(decimal), "0", "10")] decimal? Rating,
    string? Markdown,
    Dictionary<string, object?>? Frontmatter,
    string? Id = null);

public sealed record UpdateGameRequest(
    [property: StringLength(200)] string? Title,
    string? Status,
    [property: Range(typeof(decimal), "0", "10")] decimal? Rating,
    string? Markdown,
    Dictionary<string, object?>? Frontmatter,
    bool ClearRating = false);
