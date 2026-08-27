using System.ComponentModel;
using MarkdownGameTracker.Services;

namespace MarkdownGameTracker.Api;

public static class MarkdownEndpoints
{
    private const int MaximumPreviewLength = 1_000_000;

    public static IEndpointRouteBuilder MapMarkdownEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/markdown/preview",
                (MarkdownPreviewRequest request, MarkdownRenderer renderer) =>
                {
                    if (request.Markdown?.Length > MaximumPreviewLength)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["markdown"] = ["Markdown preview is limited to 1,000,000 characters."]
                        });
                    }

                    return Results.Content(renderer.Render(request.Markdown), "text/html; charset=utf-8");
                })
            .WithTags("Markdown")
            .WithName("PreviewMarkdown")
            .WithSummary("Render a Markdown preview")
            .WithDescription("Renders Markdown as safe HTML for the editor preview. Raw HTML is disabled.")
            .Accepts<MarkdownPreviewRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .ProducesValidationProblem();

        return endpoints;
    }

    public sealed record MarkdownPreviewRequest(
        [property: Description("Markdown source to render as HTML.")] string? Markdown);
}
