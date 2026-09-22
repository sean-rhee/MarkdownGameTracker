namespace MarkdownGameTracker.Services;

internal sealed record IgdbMatchOutcome(IgdbGame[] Games, bool IsComplete)
{
    public bool IsConfirmedMiss => Games.Length == 0 && IsComplete;
}

internal sealed class IgdbGameMatching(IIgdbApiClient apiClient)
{
    public async Task<IgdbMatchOutcome> FindAsync(string title, string fields,
        CancellationToken cancellationToken, bool includeAlternatives = false,
        IgdbQueryResult? exactResult = null)
    {
        title = title.Trim();
        exactResult ??= await apiClient.QueryGamesAsync(title, ExactQuery(title, fields), cancellationToken);
        var exact = exactResult.Games.Where(game =>
            string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0 && !includeAlternatives)
            return new(exact, exactResult.Succeeded);

        var search = await apiClient.QueryGamesAsync(title,
            $"search \"{Escape(title)}\"; fields {fields}; where version_parent = null; limit 25;",
            cancellationToken);
        var candidates = exact.Concat(search.Games.Where(game =>
                string.Equals(game.Name, title, StringComparison.OrdinalIgnoreCase)))
            .Concat(search.Games).DistinctBy(game => game.Id).ToArray();
        return new(candidates, exactResult.Succeeded && search.Succeeded);
    }

    public static string ExactQuery(string title, string fields) =>
        $"fields {fields}; where name = \"{Escape(title.Trim())}\"; limit 10;";

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
