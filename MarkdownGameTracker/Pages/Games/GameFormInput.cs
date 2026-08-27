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

    [Display(Name = "Notes, thoughts, or review")]
    public string? Markdown { get; set; }
}
