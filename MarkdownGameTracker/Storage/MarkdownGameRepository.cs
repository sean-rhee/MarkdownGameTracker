using System.Globalization;
using System.Text;
using MarkdownGameTracker.Models;
using Microsoft.Extensions.Options;
using static MarkdownGameTracker.Storage.GameNoteFileNames;

namespace MarkdownGameTracker.Storage;

public sealed class MarkdownGameRepository : IGameRepository
{
    private readonly string _gamesDirectory;
    private readonly GameNoteDocumentSerializer _documentSerializer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public MarkdownGameRepository(
        IOptions<VaultOptions> options,
        GameNoteDocumentSerializer documentSerializer)
    {
        _documentSerializer = documentSerializer;
        var vaultOptions = options.Value;
        var vaultPath = Path.GetFullPath(vaultOptions.Path);
        _gamesDirectory = Path.GetFullPath(Path.Combine(vaultPath, vaultOptions.GamesDirectory));

        var relativeGamesPath = Path.GetRelativePath(vaultPath, _gamesDirectory);
        if (Path.IsPathRooted(relativeGamesPath)
            || relativeGamesPath.Equals("..", StringComparison.Ordinal)
            || relativeGamesPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Vault:GamesDirectory must remain inside Vault:Path.");
        }

        Directory.CreateDirectory(_gamesDirectory);
    }

    public async Task<IReadOnlyList<GameNote>> ListAsync(
        string? status, string? search, CancellationToken cancellationToken) =>
        (await ScanAsync(status, search, cancellationToken)).Games;

    public async Task<GameLibraryScan> ScanAsync(
        string? status, string? search, CancellationToken cancellationToken)
    {
        var games = new List<GameNote>();
        var diagnostics = new List<GameNoteDiagnostic>();
        foreach (var filePath in Directory.EnumerateFiles(_gamesDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameNote game;
            try
            {
                game = await ReadAsync(filePath, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new(Path.GetFileName(filePath), exception is InvalidDataException
                    ? "Invalid YAML frontmatter. Repair this note in Obsidian before editing it here."
                    : "Could not read this note. Check its availability and permissions."));
                continue;
            }

            if (IsGame(game.Frontmatter)
                && (string.IsNullOrWhiteSpace(status)
                    || string.Equals(game.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
                && GameSearch.Matches(game, search))
            {
                games.Add(game);
            }
        }

        return new(games.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<GameNote?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var filePath = FindExistingFile(id);
        if (filePath is null)
        {
            return null;
        }

        var game = await ReadAsync(filePath, cancellationToken);
        return IsGame(game.Frontmatter) ? game : null;
    }

    public async Task<GameNote> CreateAsync(
        CreateGameRequest request,
        CancellationToken cancellationToken)
    {
        var title = ValidateDisplayTitle(request.Title);
        var status = GameStatuses.NormalizeRequired(request.Status);
        GameNoteUpdatePolicy.ValidateRating(request.Rating);
        var id = request.Id is null
            ? CreateSafeId(title)
            : ValidateId(request.Id);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (FindExistingFile(id) is not null)
            {
                throw new GameAlreadyExistsException(title);
            }

            var frontmatter = GameNoteUpdatePolicy.Apply(
                null, title, status, request.Rating, false, request.Frontmatter, _documentSerializer);
            if (!string.Equals(id, title, StringComparison.Ordinal))
            {
                frontmatter.TryAdd("aliases", new List<object?> { title });
            }
            var filePath = GetSafeFilePath(id);
            await WriteAtomicallyAsync(filePath, frontmatter, request.Markdown ?? string.Empty, cancellationToken);
            return await ReadAsync(filePath, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<GameNote?> UpdateAsync(
        string id,
        UpdateGameRequest request,
        CancellationToken cancellationToken)
    {
        var status = request.Status is null
            ? null
            : GameStatuses.NormalizeRequired(request.Status);
        GameNoteUpdatePolicy.ValidateRating(request.Rating);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var sourcePath = FindExistingFile(id);
            if (sourcePath is null)
            {
                return null;
            }

            var existing = await ReadAsync(sourcePath, cancellationToken);
            if (!IsGame(existing.Frontmatter))
            {
                return null;
            }

            var title = request.Title is null ? existing.Title : ValidateDisplayTitle(request.Title);
            var destinationId = existing.Id;
            if (!string.Equals(existing.Title, title, StringComparison.Ordinal))
            {
                var oldBaseId = CreateSafeId(existing.Title);
                var suffix = existing.Id.StartsWith(oldBaseId, StringComparison.OrdinalIgnoreCase)
                    ? existing.Id[oldBaseId.Length..]
                    : string.Empty;
                destinationId = CreateSafeId(title, suffix);
            }

            var destinationPath = GetSafeFilePath(destinationId);

            if (Directory.EnumerateFiles(_gamesDirectory, "*.md")
                .Any(path => !string.Equals(path, sourcePath, StringComparison.Ordinal)
                    && string.Equals(Path.GetFileNameWithoutExtension(path), destinationId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new GameAlreadyExistsException(title);
            }

            var mergedFrontmatter = GameNoteUpdatePolicy.Apply(
                existing.Frontmatter, title, status, request.Rating, request.ClearRating,
                request.Frontmatter, _documentSerializer);

            var markdown = request.Markdown ?? existing.Markdown;

            await WriteUpdateAsync(sourcePath, destinationPath, mergedFrontmatter, markdown, cancellationToken);

            return await ReadAsync(destinationPath, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var filePath = FindExistingFile(id);
            if (filePath is null)
            {
                return false;
            }

            var game = await ReadAsync(filePath, cancellationToken);
            if (!IsGame(game.Frontmatter))
            {
                return false;
            }

            File.Delete(filePath);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ProgressJournalUpdateResult> UpdateProgressJournalAsync(
        string id, string expectedVersion, string content, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var filePath = FindExistingFile(id);
            if (filePath is null)
            {
                return ProgressJournalUpdateResult.NotFound();
            }

            var game = await ReadAsync(filePath, cancellationToken);
            if (!IsGame(game.Frontmatter))
            {
                return ProgressJournalUpdateResult.NotFound();
            }

            if (!ProgressJournalDocument.TryReplace(game.Markdown, expectedVersion, content,
                    out var updatedMarkdown, out var error))
            {
                return error is null ? ProgressJournalUpdateResult.Changed(game)
                    : new ProgressJournalUpdateResult(false, false, game, error);
            }

            if (!string.Equals(game.Markdown, updatedMarkdown, StringComparison.Ordinal))
            {
                await WriteAtomicallyAsync(filePath,
                    game.Frontmatter.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    updatedMarkdown, cancellationToken);
            }
            return ProgressJournalUpdateResult.Updated(await ReadAsync(filePath, cancellationToken));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<GameNote> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        var contents = await File.ReadAllTextAsync(filePath, cancellationToken);
        var (frontmatter, markdown) = _documentSerializer.Parse(contents, filePath);
        var id = Path.GetFileNameWithoutExtension(filePath);
        var title = ReadString(frontmatter, GameNoteMetadata.TitleKey);

        return new GameNote(
            id,
            string.IsNullOrWhiteSpace(title) ? id : title.Trim(),
            GameStatuses.NormalizeForRead(ReadString(frontmatter, "status")),
            ReadRating(frontmatter),
            markdown,
            frontmatter,
            new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)));
    }

    private async Task WriteUpdateAsync(string sourcePath, string destinationPath,
        Dictionary<string, object?> frontmatter, string markdown, CancellationToken cancellationToken)
    {
        var renamed = !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);
        if (renamed && string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            // Stage case-only renames so both case-sensitive and case-insensitive volumes
            // end up with exactly one file using the requested spelling.
            var stagedPath = Path.Combine(_gamesDirectory, $".{Guid.NewGuid():N}.rename");
            File.Move(sourcePath, stagedPath);
            try
            {
                await WriteAtomicallyAsync(destinationPath, frontmatter, markdown, cancellationToken);
            }
            catch
            {
                File.Move(stagedPath, sourcePath);
                throw;
            }
            File.Delete(stagedPath);
            return;
        }

        await WriteAtomicallyAsync(destinationPath, frontmatter, markdown, cancellationToken);
        if (renamed)
        {
            File.Delete(sourcePath);
        }
    }

    private async Task WriteAtomicallyAsync(
        string destinationPath,
        Dictionary<string, object?> frontmatter,
        string markdown,
        CancellationToken cancellationToken)
    {
        var document = _documentSerializer.Serialize(frontmatter, markdown);

        var temporaryPath = Path.Combine(
            _gamesDirectory,
            $".{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                document,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string? FindExistingFile(string id)
    {
        var safeId = ValidateDisplayTitle(id);
        if (safeId.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("Game-note ID contains path separators.", nameof(id));
        }
        return Directory
            .EnumerateFiles(_gamesDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), safeId,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => string.Equals(Path.GetFileNameWithoutExtension(path), safeId,
                StringComparison.Ordinal))
            .FirstOrDefault();
    }

    private string GetSafeFilePath(string title)
    {
        var filePath = Path.GetFullPath(Path.Combine(_gamesDirectory, $"{title}.md"));
        if (!string.Equals(Path.GetDirectoryName(filePath), _gamesDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The title produces an invalid game-note path.", nameof(title));
        }

        return filePath;
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, object?> frontmatter,
        string key)
    {
        return frontmatter.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsGame(IReadOnlyDictionary<string, object?> frontmatter)
    {
        return string.Equals(ReadString(frontmatter, "type"), "game", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ReadRating(IReadOnlyDictionary<string, object?> frontmatter)
    {
        if (!frontmatter.TryGetValue("rating", out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            decimal number => number,
            int number => number,
            long number => number,
            double number => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
            float number => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
            string text when decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null
        };
    }
}
