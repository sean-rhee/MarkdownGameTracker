using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace MarkdownGameTracker.Services;

public sealed class MarkdownRenderer
{
    private static readonly Regex UrlAttributePattern = new(
        "(?<attribute>href|src)=\"(?<url>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdown.ToHtml(markdown, _pipeline);
        return UrlAttributePattern.Replace(html, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            return IsSafeUrl(url)
                ? match.Value
                : $"{match.Groups["attribute"].Value}=\"#\"";
        });
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.StartsWith('#')
            || url.StartsWith('/')
            || url.StartsWith("./", StringComparison.Ordinal)
            || url.StartsWith("../", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return uri.Scheme is "http" or "https" or "mailto";
    }
}
