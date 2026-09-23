using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.GenericAttributes;
using Markdig.Renderers;
using MarkdownGameTracker.Storage;

namespace MarkdownGameTracker.Services;

public sealed class MarkdownRenderer
{
    private static readonly Regex UrlAttributePattern = new(
        "(?<attribute>href|src)=\"(?<url>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly MarkdownPipeline _pipeline = CreatePipeline();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml();
        // Disabling raw HTML does not prevent arbitrary attributes on Markdown elements.
        builder.Extensions.TryRemove<GenericAttributesExtension>();
        return builder.Build();
    }

    public string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Sanitize(Markdown.ToHtml(markdown, _pipeline));
    }

    private static string Sanitize(string html) => UrlAttributePattern.Replace(html, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            return IsSafeUrl(url)
                ? match.Value
                : $"{match.Groups["attribute"].Value}=\"#\"";
        });

    public (string Journal, string OtherNotes) RenderSections(string markdown, ProgressJournalSection section)
    {
        if (!section.Exists || section.HasDuplicateHeadings)
        {
            return (string.Empty, Render(markdown));
        }

        // Resolve links, images and footnotes against the whole document before splitting its output.
        var document = Markdown.Parse(markdown, _pipeline);
        using var journal = new StringWriter();
        using var other = new StringWriter();
        var journalRenderer = new HtmlRenderer(journal);
        var otherRenderer = new HtmlRenderer(other);
        _pipeline.Setup(journalRenderer);
        _pipeline.Setup(otherRenderer);
        foreach (var block in document)
        {
            if (block.Span.Start >= section.Start && block.Span.Start < section.End)
            {
                if (block is not Markdig.Syntax.HeadingBlock { Level: 2 })
                    journalRenderer.Render(block);
            }
            else
            {
                otherRenderer.Render(block);
            }
        }
        return (Sanitize(journal.ToString()), Sanitize(other.ToString()));
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
