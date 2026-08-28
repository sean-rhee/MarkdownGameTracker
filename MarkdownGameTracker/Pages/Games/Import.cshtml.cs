using MarkdownGameTracker.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace MarkdownGameTracker.Pages.Games;

public sealed class ImportModel(
    IHltbImportService importService,
    IMemoryCache cache) : PageModel
{
    private const long MaximumUploadBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan PendingImportDuration = TimeSpan.FromMinutes(30);

    [BindProperty]
    public IFormFile? CsvFile { get; set; }

    public HltbImportPreview? Preview { get; private set; }

    public HltbImportResult? Result { get; private set; }

    public string? ImportToken { get; private set; }

    public string? FileName { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        if (CsvFile is null || CsvFile.Length == 0)
        {
            ModelState.AddModelError(nameof(CsvFile), "Choose a non-empty HLTB CSV export.");
            return Page();
        }

        if (CsvFile.Length > MaximumUploadBytes)
        {
            ModelState.AddModelError(nameof(CsvFile), "The CSV must be 5 MB or smaller.");
            return Page();
        }

        if (!Path.GetExtension(CsvFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(CsvFile), "Choose a .csv file exported from HowLongToBeat.");
            return Page();
        }

        try
        {
            await using var buffer = new MemoryStream((int)CsvFile.Length);
            await CsvFile.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            await using var previewStream = new MemoryStream(bytes, writable: false);
            Preview = await importService.PreviewAsync(previewStream, cancellationToken);
            ImportToken = Guid.NewGuid().ToString("N");
            FileName = Path.GetFileName(CsvFile.FileName);
            cache.Set(
                GetCacheKey(ImportToken),
                new PendingHltbImport(FileName, bytes),
                PendingImportDuration);
        }
        catch (InvalidDataException exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(
        string importToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(importToken)
            || !cache.TryGetValue(GetCacheKey(importToken), out PendingHltbImport? pending)
            || pending is null)
        {
            ErrorMessage = "That import preview expired. Choose the CSV again to create a fresh preview.";
            return Page();
        }

        try
        {
            await using var importStream = new MemoryStream(pending.Bytes, writable: false);
            Result = await importService.ImportAsync(importStream, cancellationToken);
            FileName = pending.FileName;
            cache.Remove(GetCacheKey(importToken));
        }
        catch (InvalidDataException exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    private static string GetCacheKey(string token) => $"hltb-import:{token}";

    private sealed record PendingHltbImport(string FileName, byte[] Bytes);
}
