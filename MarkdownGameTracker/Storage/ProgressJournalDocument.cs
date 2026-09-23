using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace MarkdownGameTracker.Storage;

public sealed record ProgressJournalSection(
    bool Exists,
    bool HasDuplicateHeadings,
    string Content,
    string OtherMarkdown,
    string Version,
    int Start = -1,
    int End = -1);

public static class ProgressJournalDocument
{
    private static readonly Regex HeadingPattern = new(
        @"^[ \t]{0,3}##[ \t]+Progress Journal(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ProgressJournalSection Read(string markdown)
    {
        var locations = FindSections(markdown);
        var version = VersionFor(locations.Count == 0 ? null : locations[0].Content);
        if (locations.Count == 0)
        {
            return new(false, false, string.Empty, markdown, version);
        }

        if (locations.Count > 1)
        {
            // Leave the full note visible until duplicate headings are resolved.
            return new(true, true, locations[0].Content.Trim('\r', '\n'), markdown, version);
        }

        var section = locations[0];
        return new(true, false, section.Content.Trim('\r', '\n'),
            markdown[..section.Start] + markdown[section.End..], version, section.Start, section.End);
    }

    public static bool ContainsAnotherMajorHeading(string content) =>
        Markdown.Parse(content).OfType<HeadingBlock>().Any(heading => heading.Level <= 2);

    public static bool TryReplace(string markdown, string expectedVersion, string content,
        out string result)
        => TryReplace(markdown, expectedVersion, content, out result, out _);

    public static bool TryReplace(string markdown, string expectedVersion, string content,
        out string result, out string? error)
    {
        error = null;
        var locations = FindSections(markdown);
        if (locations.Count > 1 || !string.Equals(
                expectedVersion, VersionFor(locations.Count == 0 ? null : locations[0].Content),
                StringComparison.Ordinal))
        {
            result = markdown;
            return false;
        }

        var newLine = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edited = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim('\n')
            .Replace("\n", newLine, StringComparison.Ordinal);

        if (locations.Count == 0)
        {
            var separator = markdown.Length == 0 || markdown.EndsWith(newLine + newLine, StringComparison.Ordinal)
                ? string.Empty
                : markdown.EndsWith(newLine, StringComparison.Ordinal) ? newLine : newLine + newLine;
            result = markdown + separator + "## Progress Journal" + newLine
                     + (edited.Length == 0 ? string.Empty : edited + newLine);
            return ValidateReplacement(markdown, edited, markdown.Length + separator.Length, result.Length, ref result, out error);
        }

        var location = locations[0];
        var following = markdown[location.End..];
        var replacement = location.ContentStart > 0 && markdown[location.ContentStart - 1] == '\n'
            ? string.Empty : newLine;
        if (edited.Length > 0)
        {
            replacement += edited + newLine;
        }
        if (following.Length > 0)
        {
            replacement += newLine;
        }
        result = markdown[..location.ContentStart] + replacement + following;
        return ValidateReplacement(markdown, edited, location.Start,
            result.Length - following.Length, ref result, out error);
    }

    private static bool ValidateReplacement(string original, string edited, int start, int end,
        ref string result, out string? error)
    {
        // Parse the assembled note: an unfinished fence or HTML block can consume the next section.
        var sections = FindSections(result);
        if (ContainsAnotherMajorHeading(edited) || sections.Count != 1
            || sections[0].Start != start || sections[0].End != end
            || sections[0].Content.Trim('\r', '\n') != edited)
        {
            result = original;
            error = "This edit changes the note's section boundaries. Close any unfinished code or HTML blocks and use ### for journal headings. If needed, edit the full note.";
            return false;
        }
        error = null;
        return true;
    }

    private static List<SectionLocation> FindSections(string markdown)
    {
        var headings = Markdown.Parse(markdown).OfType<HeadingBlock>().ToArray();
        var results = new List<SectionLocation>();
        for (var index = 0; index < headings.Length; index++)
        {
            var heading = headings[index];
            if (heading.Level != 2)
            {
                continue;
            }

            var start = markdown.LastIndexOf('\n', Math.Max(0, heading.Span.Start - 1)) + 1;
            var lineEnd = markdown.IndexOf('\n', heading.Span.Start);
            if (lineEnd < 0)
            {
                lineEnd = markdown.Length;
            }
            var headingLine = markdown[start..lineEnd].TrimEnd('\r');
            if (!HeadingPattern.IsMatch(headingLine))
            {
                continue;
            }

            var contentStart = lineEnd < markdown.Length ? lineEnd + 1 : lineEnd;
            var nextHeading = headings.Skip(index + 1).FirstOrDefault(item => item.Level <= 2);
            var end = nextHeading is null
                ? markdown.Length
                : markdown.LastIndexOf('\n', Math.Max(0, nextHeading.Span.Start - 1)) + 1;
            results.Add(new(start, contentStart, end, markdown[contentStart..end]));
        }
        return results;
    }

    private static string VersionFor(string? content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            content is null ? "missing journal" : "journal:" + content)));

    private sealed record SectionLocation(int Start, int ContentStart, int End, string Content);
}
