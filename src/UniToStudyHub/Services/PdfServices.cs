using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record PdfExtraction(
    int PageCount,
    IReadOnlyList<PdfPageData> Pages,
    bool RequiresOcr,
    IReadOnlyList<string> Warnings);

public interface IPdfTextExtractor
{
    Task<PdfExtraction> ExtractAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// PdfPig is kept behind an interface so the import workflow can be tested with
/// a deterministic fake and so a future OCR implementation can be added without
/// changing the stored document model.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<PdfExtraction> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var pages = new List<PdfPageData>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages.Add(new PdfPageData
            {
                PageNumber = page.Number,
                Text = Normalize(page.Text)
            });
        }
        var requiresOcr = pages.Count > 0 && pages.All(x => string.IsNullOrWhiteSpace(x.Text));
        var warnings = new List<string>();
        if (requiresOcr) warnings.Add("Il PDF non contiene testo estraibile: potrebbe essere scansionato. Serve OCR o un testo fornito dall'utente.");
        return Task.FromResult(new PdfExtraction(document.NumberOfPages, pages, requiresOcr, warnings));
    }

    private static string Normalize(string? text) =>
        Regex.Replace(text ?? string.Empty, "[\\t ]+", " ").Replace("\r\n", "\n").Trim();
}

public sealed class PdfImportService(IPdfTextExtractor extractor)
{
    public const long MaximumFileBytes = 25 * 1024 * 1024;
    private readonly IPdfTextExtractor _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));

    public async Task<PdfImportResult> ImportAsync(
        string path,
        StudyData data,
        string? courseId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("PDF non trovato.", path);
        var info = new FileInfo(path);
        if (info.Length == 0) throw new InvalidDataException("Il PDF è vuoto.");
        if (info.Length > MaximumFileBytes) throw new InvalidDataException("Il PDF supera il limite di 25 MB.");
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il file selezionato non ha estensione PDF.");
        await using (var header = File.OpenRead(path))
        {
            var bytes = new byte[5];
            var read = await header.ReadAsync(bytes.AsMemory(0, 5), cancellationToken);
            if (read < 5 || Encoding.ASCII.GetString(bytes) != "%PDF-")
                throw new InvalidDataException("Il file non ha una firma PDF valida.");
        }
        if (!string.IsNullOrWhiteSpace(courseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, courseId, StringComparison.Ordinal)))
            throw new ArgumentException("La materia del PDF non esiste.", nameof(courseId));

        var hash = await ComputeHashAsync(path, cancellationToken);
        var existing = data.PdfDocuments.FirstOrDefault(x => string.Equals(x.Sha256, hash, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new PdfImportResult
            {
                Document = existing,
                WasDuplicate = true,
                RequiresOcr = !existing.HasExtractableText,
                Warnings = new[] { "Il PDF è già presente nella raccolta locale; non è stato duplicato." }
            };

        var extraction = await _extractor.ExtractAsync(path, cancellationToken);
        if (extraction.PageCount < 1) throw new InvalidDataException("Il PDF non contiene pagine.");
        var document = new PdfDocumentData
        {
            FileName = Path.GetFileName(path),
            Sha256 = hash,
            PageCount = extraction.PageCount,
            ImportedAt = DateTimeOffset.Now,
            CourseId = courseId?.Trim() ?? string.Empty,
            Pages = new System.Collections.ObjectModel.ObservableCollection<PdfPageData>(extraction.Pages)
        };
        data.PdfDocuments.Add(document);
        return new PdfImportResult
        {
            Document = document,
            WasDuplicate = false,
            RequiresOcr = extraction.RequiresOcr,
            Warnings = extraction.Warnings
        };
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}

public sealed record PdfSearchHit(int PageNumber, string Snippet);

public sealed record SourceCitation(
    string DocumentId,
    string FileName,
    int PageNumber,
    string Quote);

public sealed class PdfCitationService
{
    public SourceCitation? CreateCitation(PdfDocumentData document, int pageNumber, string quote)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageNumber < 1 || pageNumber > document.PageCount) return null;
        var page = document.Pages.FirstOrDefault(x => x.PageNumber == pageNumber);
        if (page is null || string.IsNullOrWhiteSpace(page.Text)) return null;
        var normalizedQuote = Normalize(quote);
        if (normalizedQuote.Length < 3 || !Normalize(page.Text).Contains(normalizedQuote, StringComparison.OrdinalIgnoreCase))
            return null;
        return new SourceCitation(document.Id, document.FileName, pageNumber, quote.Trim());
    }

    public bool IsValid(PdfDocumentData document, SourceCitation citation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(citation);
        return string.Equals(document.Id, citation.DocumentId, StringComparison.Ordinal) &&
               CreateCitation(document, citation.PageNumber, citation.Quote) is not null;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
}

public sealed class PdfIntelligenceService
{
    private readonly PdfCitationService _citations = new();

    public IReadOnlyList<PdfSearchHit> Search(PdfDocumentData document, string query, int maxHits = 20)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(query) || maxHits < 1) return Array.Empty<PdfSearchHit>();
        var normalizedQuery = Normalize(query);
        return document.Pages
            .Where(x => Normalize(x.Text).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(maxHits)
            .Select(x => new PdfSearchHit(x.PageNumber, Snippet(x.Text, normalizedQuery)))
            .ToList();
    }

    public SourceCitation? Cite(PdfDocumentData document, int pageNumber, string quote) =>
        _citations.CreateCitation(document, pageNumber, quote);

    public bool Verify(PdfDocumentData document, SourceCitation citation) =>
        _citations.IsValid(document, citation);

    private static string Snippet(string text, string query)
    {
        var normalized = Normalize(text);
        var index = normalized.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return normalized.Length <= 240 ? normalized : normalized[..240] + "…";
        var start = Math.Max(0, index - 90);
        var length = Math.Min(normalized.Length - start, 240);
        return (start > 0 ? "…" : string.Empty) + normalized.Substring(start, length) +
               (start + length < normalized.Length ? "…" : string.Empty);
    }

    private static string Normalize(string value) =>
        Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
}
