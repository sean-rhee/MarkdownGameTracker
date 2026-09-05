using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using Microsoft.Extensions.Options;

namespace MarkdownGameTracker.Storage;

public sealed class MarkdownGameRepository : IGameRepository
{
    private static readonly char[] InvalidTitleCharacters =
        Path.GetInvalidFileNameChars().Concat(['/', '\\']).Distinct().ToArray();

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

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
        string? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var games = new List<GameNote>();

        foreach (var filePath in Directory.EnumerateFiles(_gamesDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = await ReadAsync(filePath, cancellationToken);

            if (!IsGame(game.Frontmatter))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(game.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (!game.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    && !game.Markdown.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            games.Add(game);
        }

        return games
            .OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        ValidateRating(request.Rating);
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

            var frontmatter = PrepareFrontmatter(
                request.Frontmatter,
                status,
                request.Rating,
                includeDefaults: true);
            frontmatter[GameNoteMetadata.TitleKey] = title;
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
        ValidateRating(request.Rating);

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

            if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
                && FindExistingFile(destinationId) is not null)
            {
                throw new GameAlreadyExistsException(title);
            }

            var mergedFrontmatter = existing.Frontmatter.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);

            if (request.Frontmatter is not null)
            {
                foreach (var pair in request.Frontmatter)
                {
                    if (pair.Key.Equals("status", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (pair.Value is null)
                    {
                        mergedFrontmatter.Remove(pair.Key);
                    }
                    else
                    {
                        mergedFrontmatter[pair.Key] = _documentSerializer.NormalizeValue(pair.Value);
                    }
                }
            }

            mergedFrontmatter["type"] = "game";
            mergedFrontmatter[GameNoteMetadata.TitleKey] = title;
            mergedFrontmatter.TryAdd("hobby", new List<object?> { "[[Gaming]]" });
            if (status is not null)
            {
                mergedFrontmatter["status"] = status;
            }

            if (request.Rating is not null)
            {
                mergedFrontmatter["rating"] = request.Rating;
            }

            var markdown = request.Markdown ?? existing.Markdown;

            await WriteAtomicallyAsync(destinationPath, mergedFrontmatter, markdown, cancellationToken);
            if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(sourcePath);
            }

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
        var safeId = ValidateId(id);
        return Directory
            .EnumerateFiles(_gamesDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                safeId,
                StringComparison.OrdinalIgnoreCase));
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

    public static string CreateSafeId(string title, string suffix = "")
    {
        var displayTitle = ValidateDisplayTitle(title);
        var builder = new StringBuilder(displayTitle.Length + suffix.Length);
        foreach (var character in displayTitle)
        {
            if (InvalidTitleCharacters.Contains(character))
            {
                builder.Append(character == ':' ? " -" : " ");
            }
            else
            {
                builder.Append(character);
            }
        }

        var safeBase = Regex.Replace(builder.ToString(), @"\s+", " ").Trim(' ', '.');
        if (safeBase.Length == 0)
        {
            safeBase = "Game";
        }

        if (ReservedWindowsNames.Contains(safeBase))
        {
            safeBase = $"Game - {safeBase}";
        }

        var maximumBaseLength = 200 - suffix.Length;
        if (maximumBaseLength <= 0)
        {
            throw new ArgumentException("The note filename suffix is too long.", nameof(suffix));
        }

        if (safeBase.Length > maximumBaseLength)
        {
            safeBase = safeBase[..maximumBaseLength].TrimEnd(' ', '.');
        }

        return ValidateId($"{safeBase}{suffix}");
    }

    private static string ValidateDisplayTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (trimmed.Length > 200)
        {
            throw new ArgumentException("Title cannot be longer than 200 characters.", nameof(title));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Title contains unsupported control characters.", nameof(title));
        }

        if (trimmed is "." or ".."
            || trimmed.Contains("../", StringComparison.Ordinal)
            || trimmed.Contains("..\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Title contains an unsafe path sequence.", nameof(title));
        }

        return trimmed;
    }

    private static string ValidateId(string? id)
    {
        var trimmed = id?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Game-note ID is required.", nameof(id));
        }

        if (trimmed.Length > 200)
        {
            throw new ArgumentException("Game-note ID cannot be longer than 200 characters.", nameof(id));
        }

        if (trimmed is "." or ".." || trimmed.IndexOfAny(InvalidTitleCharacters) >= 0)
        {
            throw new ArgumentException("Game-note ID contains invalid filename characters.", nameof(id));
        }

        if (ReservedWindowsNames.Contains(trimmed.TrimEnd('.')))
        {
            throw new ArgumentException("Game-note ID is reserved by the operating system.", nameof(id));
        }

        return trimmed;
    }

    private static void ValidateRating(decimal? rating)
    {
        if (rating is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 10.");
        }
    }

    private Dictionary<string, object?> PrepareFrontmatter(
        IReadOnlyDictionary<string, object?>? source,
        string? status,
        decimal? rating,
        bool includeDefaults)
    {
        var frontmatter = source?.ToDictionary(
                pair => pair.Key,
                pair => _documentSerializer.NormalizeValue(pair.Value),
                StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        if (includeDefaults)
        {
            frontmatter["type"] = "game";
            frontmatter.TryAdd("hobby", new List<object?> { "[[Gaming]]" });
        }

        SetOrRemove(frontmatter, "status", string.IsNullOrWhiteSpace(status) ? null : status.Trim());
        SetOrRemove(frontmatter, "rating", rating);
        return frontmatter;
    }

    private static void SetOrRemove(
        IDictionary<string, object?> frontmatter,
        string key,
        object? value)
    {
        if (value is null)
        {
            frontmatter.Remove(key);
        }
        else
        {
            frontmatter[key] = value;
        }
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
