namespace MarkdownGameTracker.Models;

public static class GameStatuses
{
    public const string Active = "active";
    public const string Endless = "endless";
    public const string Completed = "completed";
    public const string Inactive = "inactive";
    public const string Planned = "planned";

    public static IReadOnlyList<string> All { get; } =
        [Active, Endless, Completed, Inactive, Planned];

    public static string GetLabel(string status) => status switch
    {
        Active => "Active",
        Endless => "Endless",
        Completed => "Completed",
        Inactive => "Inactive",
        Planned => "Plan to play",
        _ => status
    };

    public static bool TryNormalize(string? status, out string normalized)
    {
        normalized = status?.Trim().ToLowerInvariant() switch
        {
            Active => Active,
            Endless => Endless,
            Completed => Completed,
            Inactive => Inactive,
            Planned or "plan to play" or "planning" or "backlog" => Planned,
            _ => string.Empty
        };

        return normalized.Length > 0;
    }

    public static string NormalizeRequired(string? status)
    {
        if (TryNormalize(status, out var normalized))
        {
            return normalized;
        }

        throw new ArgumentException(
            "Status must be active, endless, completed, inactive, or planned.",
            nameof(status));
    }

    public static string? NormalizeForRead(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return TryNormalize(status, out var normalized)
            ? normalized
            : status.Trim();
    }
}
