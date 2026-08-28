using System.Globalization;
using System.ComponentModel.DataAnnotations;
using MarkdownGameTracker.Models;

namespace MarkdownGameTracker.Pages.Games;

public sealed class GameFormInput
{
    [Required]
    [StringLength(200)]
    [Display(Name = "Game title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = GameStatuses.Planned;

    [Range(typeof(decimal), "0", "10")]
    public decimal? Rating { get; set; }

    [StringLength(100)]
    public string? Platform { get; set; }

    [Display(Name = "Start date")]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "Completion date")]
    [DataType(DataType.Date)]
    public DateOnly? CompletionDate { get; set; }

    [Display(Name = "Notes, thoughts, or review")]
    public string? Markdown { get; set; }

    public Dictionary<string, object?> ToFrontmatter() => new(StringComparer.Ordinal)
    {
        [GameNoteMetadata.PlatformKey] = string.IsNullOrWhiteSpace(Platform) ? null : Platform.Trim(),
        [GameNoteMetadata.StartDateKey] = StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        [GameNoteMetadata.CompletionDateKey] = CompletionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };
}
