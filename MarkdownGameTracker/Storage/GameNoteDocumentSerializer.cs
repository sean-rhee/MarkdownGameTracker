using System.Globalization;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace MarkdownGameTracker.Storage;

public sealed class GameNoteDocumentSerializer
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
    private readonly ISerializer _serializer = new SerializerBuilder().Build();

    public ParsedGameNoteDocument Parse(string contents, string filePath)
    {
        var normalized = contents.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new(new Dictionary<string, object?>(StringComparer.Ordinal), normalized);
        }

        var closingDelimiter = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var markdownStart = closingDelimiter >= 0 ? closingDelimiter + 5 : normalized.Length;

        if (closingDelimiter < 0 && normalized.EndsWith("\n---", StringComparison.Ordinal))
        {
            closingDelimiter = normalized.Length - 4;
        }

        if (closingDelimiter < 0)
        {
            throw new InvalidDataException($"The note '{Path.GetFileName(filePath)}' has unclosed YAML frontmatter.");
        }

        var yaml = normalized[4..closingDelimiter];
        var markdown = normalized[markdownStart..];

        try
        {
            var raw = string.IsNullOrWhiteSpace(yaml)
                ? new Dictionary<object, object?>()
                : _deserializer.Deserialize<Dictionary<object, object?>>(yaml)
                    ?? new Dictionary<object, object?>();

            return new(NormalizeDictionary(raw), markdown);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"The note '{Path.GetFileName(filePath)}' contains invalid YAML frontmatter.",
                exception);
        }
    }

    public string Serialize(IReadOnlyDictionary<string, object?> frontmatter, string markdown)
    {
        var yaml = _serializer.Serialize(frontmatter).TrimEnd();
        var normalizedMarkdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\n');
        return new StringBuilder()
            .AppendLine("---")
            .AppendLine(yaml)
            .AppendLine("---")
            .Append(normalizedMarkdown)
            .ToString();
    }

    public object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element => NormalizeJsonElement(element),
            IDictionary<object, object?> dictionary => NormalizeDictionary(dictionary),
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeValue(pair.Value),
                StringComparer.Ordinal),
            IEnumerable<object?> sequence when value is not string => sequence.Select(NormalizeValue).ToList(),
            _ => value
        };
    }

    private Dictionary<string, object?> NormalizeDictionary(IDictionary<object, object?> source)
    {
        return source.ToDictionary(
            pair => Convert.ToString(pair.Key, CultureInfo.InvariantCulture) ?? string.Empty,
            pair => NormalizeValue(pair.Value),
            StringComparer.Ordinal);
    }

    private object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => NormalizeJsonElement(property.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}

public sealed record ParsedGameNoteDocument(
    Dictionary<string, object?> Frontmatter,
    string Markdown);
