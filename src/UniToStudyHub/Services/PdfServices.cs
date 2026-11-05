using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public sealed record PdfExtraction(
    int PageCount,
    IReadOnlyList<PdfPageData> Pages,
    bool RequiresOcr,
    IReadOnlyList<string> Warnings);

public interface IPdfTextExtractor
{
    Task<PdfExtraction> ExtractAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Default stream entry point: keeps the legacy path-based contract working
    /// while allowing an in-memory implementation that never writes the
    /// document to disk.
    /// </summary>
    async Task<PdfExtraction> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var directory = Path.Combine(Path.GetTempPath(), "unitracker-pdf");
        LocalFileSecurity.EnsurePrivateDirectory(directory);
        var temporary = Path.Combine(directory, "extract-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            return await ExtractAsync(temporary, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LocalFileSecurity.ScrubAndDelete(temporary);
        }
    }
}

/// <summary>
/// PdfPig extraction behind an explicit, bounded contract.
///
/// The document is parsed from memory, page count and extracted text are capped
/// so a malicious PDF cannot exhaust memory or the UI, control characters are
/// removed, and every parser failure becomes a generic <see cref="InvalidDataException"/>:
/// the text of a PDF is untrusted data, never an instruction and never a
/// reason to surface a raw parser stack trace.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public const int MaximumPageCharacters = 200_000;
    public const long MaximumTotalCharacters = 4_000_000;

    public Task<PdfExtraction> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("PDF non trovato.", path);
        var info = new FileInfo(path);
        if (info.Length > PdfImportService.MaximumFileBytes)
            throw new InvalidDataException("Il PDF supera il limite di 25 MB.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ExtractAsync(stream, cancellationToken);
    }

    public async Task<PdfExtraction> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0) throw new InvalidDataException("Il PDF è vuoto.");
        if (buffer.Length > PdfImportService.MaximumFileBytes)
            throw new InvalidDataException("Il PDF supera il limite di 25 MB.");
        var bytes = buffer.ToArray();
        try
        {
            return ExtractFromBytes(bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static PdfExtraction ExtractFromBytes(byte[] bytes, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
            PdfImportService.EnsurePageLimit(document.NumberOfPages);
            var pages = new List<PdfPageData>();
            long totalCharacters = 0;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = Normalize(page.Text);
                if (text.Length > MaximumPageCharacters)
                {
                    text = text[..MaximumPageCharacters];
                    warnings.Add($"Pagina {page.Number} troncata al limite di {MaximumPageCharacters} caratteri.");
                }
                totalCharacters += text.Length;
                pages.Add(new PdfPageData { PageNumber = page.Number, Text = text });
                if (totalCharacters >= MaximumTotalCharacters)
                {
                    warnings.Add("Estrazione interrotta al limite di testo consentito; usa un intervallo di pagine.");
                    break;
                }
            }

            var requiresOcr = pages.Count > 0 && pages.All(page => string.IsNullOrWhiteSpace(page.Text));
            if (requiresOcr)
                warnings.Add(
                    "Il PDF non contiene testo estraibile: potrebbe essere scansionato. Serve OCR o un testo fornito dall'utente.");
            return new PdfExtraction(document.NumberOfPages, pages, requiresOcr, warnings.ToArray());
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("pdf", "Estrazione PDF non riuscita.", ex);
            throw new InvalidDataException(
                "Il PDF non può essere letto in modo sicuro: il file potrebbe essere danneggiato, protetto o non supportato.",
                ex);
        }
    }

    private static string Normalize(string? text) =>
        Regex.Replace(text ?? string.Empty, "[\\t ]+", " ").Replace("\r\n", "\n").Trim();
}

public sealed class PdfImportService(IPdfTextExtractor extractor)
{
    public const long MaximumFileBytes = 25 * 1024 * 1024;
    public const int MaximumPageCount = 5000;
    public const int MaximumFileNameLength = 200;

    /// <summary>
    /// Rejects an absurd page count before the parser allocates per-page text.
    /// Enforced by the importer as well as the PdfPig extractor so a different
    /// extractor implementation cannot bypass it.
    /// </summary>
    public static void EnsurePageLimit(int pageCount)
    {
        if (pageCount > MaximumPageCount)
            throw new InvalidDataException($"Il PDF supera il limite di {MaximumPageCount} pagine.");
    }
    private readonly IPdfTextExtractor _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));

    /// <summary>
    /// Imports a PDF exactly once (deduplicated by SHA-256).
    ///
    /// The file is opened once, read into a bounded buffer and hashed from those
    /// same bytes, so the hash and the parsed content cannot diverge. The
    /// extension, the size and the <c>%PDF-</c> signature are all required; the
    /// stored file name is reduced to a safe leaf name. Nothing is executed and
    /// no document text is logged.
    /// </summary>
    public async Task<PdfImportResult> ImportAsync(
        string path,
        StudyData data,
        string? courseId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("PDF non trovato.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("PDF non trovato.", fullPath);
        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Il percorso selezionato è una cartella, non un PDF.");
        if (!string.Equals(info.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il file selezionato non ha estensione PDF.");
        if (info.Length == 0) throw new InvalidDataException("Il PDF è vuoto.");
        if (info.Length > MaximumFileBytes) throw new InvalidDataException("Il PDF supera il limite di 25 MB.");

        byte[] bytes;
        await using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Il PDF supera il limite di 25 MB.");
            using var buffer = new MemoryStream((int)stream.Length);
            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        try
        {
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                throw new InvalidDataException("Il file non ha una firma PDF valida.");

            if (!string.IsNullOrWhiteSpace(courseId) &&
                !data.Courses.Any(course => string.Equals(course.Id, courseId, StringComparison.Ordinal)))
                throw new ArgumentException("La materia del PDF non esiste.", nameof(courseId));

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var existing = data.PdfDocuments.FirstOrDefault(
                document => string.Equals(document.Sha256, hash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return new PdfImportResult
                {
                    Document = existing,
                    WasDuplicate = true,
                    RequiresOcr = !existing.HasExtractableText,
                    Warnings = new[] { "Il PDF è già presente nella raccolta locale; non è stato duplicato." }
                };
            }

            using var content = new MemoryStream(bytes, writable: false);
            var extraction = await _extractor.ExtractAsync(content, cancellationToken).ConfigureAwait(false);
            if (extraction.PageCount < 1) throw new InvalidDataException("Il PDF non contiene pagine.");
            EnsurePageLimit(extraction.PageCount);

            var document = new PdfDocumentData
            {
                FileName = SafeFileName(fullPath),
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
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string SafeFileName(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        var name = Path.GetFileName(normalized);
        var sanitized = new string(name
            .Where(character => !char.IsControl(character) && character is not '/' and not '\\' and not ':')
            .ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized)) return "documento.pdf";
        return sanitized.Length <= MaximumFileNameLength ? sanitized : sanitized[..MaximumFileNameLength];
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
        if (normalizedQuote.Length < 3 ||
            !Normalize(page.Text).Contains(normalizedQuote, StringComparison.OrdinalIgnoreCase))
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
            .Where(page => Normalize(page.Text).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(maxHits)
            .Select(page => new PdfSearchHit(page.PageNumber, Snippet(page.Text, normalizedQuery)))
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
