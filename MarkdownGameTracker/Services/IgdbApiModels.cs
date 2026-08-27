using System.Text.Json.Serialization;

namespace MarkdownGameTracker.Services;

internal sealed record IgdbGame(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("storyline")] string? Storyline,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("first_release_date")] long? FirstReleaseDate,
    [property: JsonPropertyName("cover")] IgdbImage? Cover,
    [property: JsonPropertyName("artworks")] IgdbImage[]? Artworks,
    [property: JsonPropertyName("screenshots")] IgdbImage[]? Screenshots,
    [property: JsonPropertyName("videos")] IgdbGameVideo[]? Videos);

internal sealed record IgdbImage(
    [property: JsonPropertyName("image_id")] string ImageId,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height);

internal sealed record IgdbGameVideo(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("video_id")] string VideoId);

internal sealed record IgdbQueryResult(bool Succeeded, IgdbGame[] Games)
{
    public static IgdbQueryResult Success(IgdbGame[] games) => new(true, games);

    public static IgdbQueryResult Failed() => new(false, []);
}

internal sealed record IgdbNamedGameResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("result")] IgdbGame[] Result);

internal sealed record IgdbMultiQueryResult(bool Succeeded, IgdbNamedGameResult[] Results)
{
    public static IgdbMultiQueryResult Success(IgdbNamedGameResult[] results) => new(true, results);

    public static IgdbMultiQueryResult Failed() => new(false, []);
}
