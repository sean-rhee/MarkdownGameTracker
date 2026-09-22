using System.Globalization;
using System.Text;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Storage;
using Microsoft.VisualBasic.FileIO;

namespace MarkdownGameTracker.Services;

public interface IHltbImportService
{
    Task<HltbImportPreview> PreviewAsync(Stream csv, CancellationToken cancellationToken);

    Task<HltbImportResult> ImportAsync(Stream csv, CancellationToken cancellationToken);
}

public sealed class HltbImportService(IGameRepository repository) : IHltbImportService
{
    private const int MaximumRows = 10_000;
    private static readonly IReadOnlyDictionary<string, string> StatusMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Playing"] = GameStatuses.Active,
            ["Backlog"] = GameStatuses.Planned,
            ["Endless"] = GameStatuses.Endless,
            ["Dropped"] = GameStatuses.Inactive,
            ["Completed"] = GameStatuses.Completed,
            ["Retired"] = GameStatuses.Inactive
        };

    public async Task<HltbImportPreview> PreviewAsync(
        Stream csv,
        CancellationToken cancellationToken)
    {
        var drafts = Parse(csv, cancellationToken);
        var existingGames = await repository.ListAsync(null, null, cancellationToken);
        var entries = AssignIdsAndFindConflicts(drafts, existingGames);
        return new HltbImportPreview(entries);
    }

    public async Task<HltbImportResult> ImportAsync(
        Stream csv,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(csv, cancellationToken);
        var imported = new List<string>();
        var errors = new List<string>();

        foreach (var entry in preview.Entries.Where(item => item.Disposition == HltbImportDisposition.Ready))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await repository.CreateAsync(
                    new CreateGameRequest(
                        entry.Title,
                        entry.Status,
                        null,
                        entry.Markdown,
                        BuildFrontmatter(entry),
                        entry.ProposedId),
                    cancellationToken);
                imported.Add(entry.ProposedId);
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or GameAlreadyExistsException)
            {
                errors.Add($"Row {entry.RowNumber} ({entry.Title}): {exception.Message}");
            }
        }

        return new HltbImportResult(
            imported,
            preview.Entries.Count - preview.ReadyCount,
            errors);
    }

    private static Dictionary<string, object?> BuildFrontmatter(HltbImportEntry entry)
    {
        var frontmatter = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = "howlongtobeat",
            [GameNoteMetadata.PlatformKey] = entry.Platform,
            [GameNoteMetadata.StartDateKey] = entry.StartDate,
            [GameNoteMetadata.CompletionDateKey] = entry.CompletionDate,
            ["hltb_added"] = entry.Added,
            ["hltb_updated"] = entry.Updated
        };

        return frontmatter
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static List<HltbImportDraft> Parse(Stream csv, CancellationToken cancellationToken)
    {
        var drafts = new List<HltbImportDraft>();
        using var parser = new TextFieldParser(csv, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()
                      ?? throw new InvalidDataException("The CSV is empty.");
        var indexes = headers
            .Select((header, index) => new { Header = header.Trim(), Index = index })
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        RequireHeaders(indexes, ["Title", "Platform", .. StatusMappings.Keys]);

        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            if (drafts.Count >= MaximumRows)
            {
                throw new InvalidDataException($"The CSV contains more than {MaximumRows:N0} rows.");
            }

            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException exception)
            {
                throw new InvalidDataException($"Row {rowNumber} is not valid CSV.", exception);
            }

            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var warnings = new List<string>();
            var title = Get(fields, indexes, "Title")?.Trim() ?? string.Empty;
            var platform = NullIfWhiteSpace(Get(fields, indexes, "Platform"));
            var selectedStatusFlags = StatusMappings
                .Where(mapping => string.Equals(Get(fields, indexes, mapping.Key), "X", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var status = selectedStatusFlags.Length == 1 ? selectedStatusFlags[0].Value : null;
            if (title.Length == 0)
            {
                warnings.Add("Title is missing.");
            }

            if (selectedStatusFlags.Length == 0)
            {
                warnings.Add("No supported HLTB status is selected.");
            }
            else if (selectedStatusFlags.Length > 1)
            {
                warnings.Add("More than one HLTB status is selected.");
            }

            var startDate = ParseDate(Get(fields, indexes, "Start Date"), "start date", warnings);
            var completionDate = ParseDate(Get(fields, indexes, "Completion Date"), "completion date", warnings);
            var markdown = BuildMarkdown(
                NullIfWhiteSpace(Get(fields, indexes, "General Notes")),
                NullIfWhiteSpace(Get(fields, indexes, "Review Notes")));

            drafts.Add(new HltbImportDraft(
                rowNumber,
                title,
                platform,
                status,
                startDate,
                completionDate,
                NullIfWhiteSpace(Get(fields, indexes, "Added")),
                NullIfWhiteSpace(Get(fields, indexes, "Updated")),
                markdown,
                warnings));
        }

        return drafts;
    }

    private static IReadOnlyList<HltbImportEntry> AssignIdsAndFindConflicts(
        IReadOnlyList<HltbImportDraft> drafts,
        IReadOnlyList<GameNote> existingGames)
    {
        var baseIds = new Dictionary<int, string>();
        var invalidEntries = new Dictionary<int, HltbImportEntry>();
        foreach (var draft in drafts)
        {
            if (draft.Title.Length == 0 || draft.Status is null)
            {
                invalidEntries[draft.RowNumber] = ToEntry(draft, string.Empty, HltbImportDisposition.Invalid, null);
                continue;
            }

            try
            {
                baseIds[draft.RowNumber] = GameNoteFileNames.CreateSafeId(draft.Title);
            }
            catch (ArgumentException exception)
            {
                invalidEntries[draft.RowNumber] = InvalidFilenameEntry(draft, exception);
            }
        }

        var validDrafts = drafts.Where(draft => baseIds.ContainsKey(draft.RowNumber)).ToArray();
        var titleCounts = validDrafts
            .GroupBy(draft => draft.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var baseIdCounts = validDrafts
            .GroupBy(draft => baseIds[draft.RowNumber], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var claimedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<HltbImportEntry>(drafts.Count);

        foreach (var draft in drafts)
        {
            if (invalidEntries.TryGetValue(draft.RowNumber, out var invalidEntry))
            {
                results.Add(invalidEntry);
                continue;
            }

            var sourceMatch = !string.IsNullOrWhiteSpace(draft.Added)
                ? existingGames.FirstOrDefault(game => string.Equals(
                    GameNoteMetadata.GetString(game, "hltb_added"),
                    draft.Added,
                    StringComparison.Ordinal))
                : null;
            var singleTitleMatch = titleCounts[draft.Title] == 1
                ? existingGames.FirstOrDefault(game =>
                    string.Equals(game.Title, draft.Title, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(GameNoteMetadata.GetString(game, GameNoteMetadata.PlatformKey))
                        || string.Equals(
                            GameNoteMetadata.GetString(game, GameNoteMetadata.PlatformKey),
                            draft.Platform,
                            StringComparison.OrdinalIgnoreCase)))
                : null;
            var existingMatch = sourceMatch ?? singleTitleMatch;
            if (existingMatch is not null)
            {
                results.Add(ToEntry(
                    draft,
                    existingMatch.Id,
                    HltbImportDisposition.Conflict,
                    $"Existing note: {existingMatch.Id}.md"));
                continue;
            }

            var baseId = baseIds[draft.RowNumber];
            var usePlatformSuffix = baseIdCounts[baseId] > 1
                                    || existingGames.Any(game => string.Equals(game.Id, baseId, StringComparison.OrdinalIgnoreCase));
            var ordinal = 1;
            string proposedId;
            try
            {
                proposedId = BuildProposedId(draft.Title, draft.Platform, usePlatformSuffix, ordinal);
                while (claimedIds.Contains(proposedId)
                       || existingGames.Any(game => string.Equals(game.Id, proposedId, StringComparison.OrdinalIgnoreCase)))
                {
                    ordinal++;
                    proposedId = BuildProposedId(draft.Title, draft.Platform, usePlatformSuffix, ordinal);
                }
            }
            catch (ArgumentException exception)
            {
                results.Add(InvalidFilenameEntry(draft, exception));
                continue;
            }

            claimedIds.Add(proposedId);
            results.Add(ToEntry(draft, proposedId, HltbImportDisposition.Ready, null));
        }

        return results;
    }

    private static HltbImportEntry InvalidFilenameEntry(HltbImportDraft draft, ArgumentException exception) =>
        ToEntry(
            draft with { Warnings = [.. draft.Warnings, $"Invalid note filename: {exception.Message}"] },
            string.Empty,
            HltbImportDisposition.Invalid,
            null);

    private static string BuildProposedId(
        string title,
        string? platform,
        bool usePlatformSuffix,
        int ordinal)
    {
        var suffix = string.Empty;
        if (usePlatformSuffix)
        {
            var safePlatform = string.IsNullOrWhiteSpace(platform)
                ? "Unknown platform"
                : GameNoteFileNames.CreateSafeId(platform);
            suffix = ordinal == 1
                ? $" ({safePlatform})"
                : $" ({safePlatform}, {ordinal})";
        }
        else if (ordinal > 1)
        {
            suffix = $" ({ordinal})";
        }

        return GameNoteFileNames.CreateSafeId(title, suffix);
    }

    private static HltbImportEntry ToEntry(
        HltbImportDraft draft,
        string proposedId,
        string disposition,
        string? conflict) =>
        new(
            draft.RowNumber,
            draft.Title,
            draft.Platform,
            draft.Status,
            proposedId,
            draft.StartDate,
            draft.CompletionDate,
            draft.Added,
            draft.Updated,
            draft.Markdown,
            disposition,
            conflict,
            draft.Warnings);

    private static void RequireHeaders(
        IReadOnlyDictionary<string, int> indexes,
        IReadOnlyList<string> required)
    {
        var missing = required.Where(header => !indexes.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"The CSV is missing required columns: {string.Join(", ", missing)}.");
        }
    }

    private static string? Get(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> indexes,
        string header) =>
        indexes.TryGetValue(header, out var index) && index < fields.Count
            ? fields[index]
            : null;

    private static string? ParseDate(string? value, string label, ICollection<string> warnings)
    {
        var normalized = NullIfWhiteSpace(value);
        if (normalized is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        warnings.Add($"Ignored invalid {label} '{normalized}'.");
        return null;
    }

    private static string BuildMarkdown(string? generalNotes, string? reviewNotes)
    {
        var sections = new List<string>();
        if (generalNotes is not null)
        {
            sections.Add($"## HLTB notes\n\n{generalNotes}");
        }

        if (reviewNotes is not null)
        {
            sections.Add($"## HLTB review notes\n\n{reviewNotes}");
        }

        return sections.Count == 0 ? "## Thoughts\n" : $"{string.Join("\n\n", sections)}\n";
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record HltbImportDraft(
        int RowNumber,
        string Title,
        string? Platform,
        string? Status,
        string? StartDate,
        string? CompletionDate,
        string? Added,
        string? Updated,
        string Markdown,
        IReadOnlyList<string> Warnings);
}

public sealed record HltbImportPreview(IReadOnlyList<HltbImportEntry> Entries)
{
    public int TotalCount => Entries.Count;
    public int ReadyCount => Entries.Count(entry => entry.Disposition == HltbImportDisposition.Ready);
    public int ConflictCount => Entries.Count(entry => entry.Disposition == HltbImportDisposition.Conflict);
    public int InvalidCount => Entries.Count(entry => entry.Disposition == HltbImportDisposition.Invalid);
    public int WarningCount => Entries.Sum(entry => entry.Warnings.Count);
}

public sealed record HltbImportEntry(
    int RowNumber,
    string Title,
    string? Platform,
    string? Status,
    string ProposedId,
    string? StartDate,
    string? CompletionDate,
    string? Added,
    string? Updated,
    string Markdown,
    string Disposition,
    string? Conflict,
    IReadOnlyList<string> Warnings);

public static class HltbImportDisposition
{
    public const string Ready = "ready";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
}

public sealed record HltbImportResult(
    IReadOnlyList<string> ImportedIds,
    int SkippedCount,
    IReadOnlyList<string> Errors)
{
    public int ImportedCount => ImportedIds.Count;
}
