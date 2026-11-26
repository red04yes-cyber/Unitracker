using System.IO;
using System.Security.Cryptography;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public enum DocumentLibraryStatus
{
    Available,
    Missing,
    Moved,
    UpdateRequired,
    Unsupported,
    UnsafePath,
    Unavailable
}

public sealed record DocumentLibraryEntry(
    string CourseId,
    string CourseName,
    string SectionName,
    string ModuleName,
    string Category,
    string FileName,
    string Extension,
    long? RemoteSizeBytes,
    long? LocalSizeBytes,
    DateTimeOffset? RemoteModifiedUtc,
    DateTimeOffset? LastModifiedUtc,
    string? Sha256,
    string ExpectedPath,
    string? LocalPath,
    DocumentLibraryStatus Status,
    bool IsMoved,
    bool HasAmbiguousLocation);

public sealed record DocumentLibraryIndexResult(
    IReadOnlyList<DocumentLibraryEntry> Entries,
    IReadOnlyList<string> Warnings,
    bool IsTruncated,
    bool LocationScanComplete,
    long BytesHashed);

public sealed record DocumentLibraryFilter(
    string? SearchText = null,
    string? CourseId = null,
    string? Category = null,
    string? Extension = null,
    DocumentLibraryStatus? Status = null,
    DateTimeOffset? ModifiedFromUtc = null,
    DateTimeOffset? ModifiedThroughUtc = null);

/// <summary>
/// Builds a read-only catalogue by reconciling the current Moodle file
/// descriptors with the course folder on disk. It does not persist a second
/// index: Moodle remains the remote catalogue and the filesystem remains the
/// source of truth for local availability.
/// </summary>
public sealed class DocumentLibraryService
{
    public const int MaximumLocalEntriesToScan = 5_000;
    public const long MaximumHashBytesPerCourse = 1024L * 1024 * 1024;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public async Task<DocumentLibraryIndexResult> IndexCourseAsync(
        Course course,
        IEnumerable<MoodleCourseSection> sections,
        IEnumerable<MoodleContentFile>? additionalFiles = null,
        string? documentsBasePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(sections);
        if (string.IsNullOrWhiteSpace(course.Id))
            throw new ArgumentException("A course identifier is required.", nameof(course));

        cancellationToken.ThrowIfCancellationRequested();
        var courseRoot = DocumentSyncService.GetCourseDirectory(course, documentsBasePath);
        var sourceSet = DocumentSyncService.EnumerateSources(sections, additionalFiles, cancellationToken);
        var warnings = new List<string>();
        if (sourceSet.Truncated)
            warnings.Add($"L'indice è limitato ai primi {DocumentSyncService.MaximumFilesPerCourse} materiali del corso.");
        if (sourceSet.Rejected > 0)
            warnings.Add("Alcuni allegati Moodle sono stati ignorati perché incompleti o oltre i limiti consentiti.");

        var localFiles = ScanLocalFiles(courseRoot, warnings, cancellationToken);
        var entries = new List<DocumentLibraryEntry>(sourceSet.Files.Count);
        long bytesHashed = 0;
        var hashBudgetWarningAdded = false;

        foreach (var source in sourceSet.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = source.File;
            var fileName = DocumentSyncService.SafeName(file.filename);
            var category = DocumentSyncService.Categorize(file.filename, file.mimetype);
            var relativePath = DocumentSyncService.GetRelativePath(source);
            var expectedPath = Path.GetFullPath(Path.Combine(courseRoot, relativePath, fileName));
            var remoteModified = file.timemodified > 0
                ? DateTimeOffset.FromUnixTimeSeconds(file.timemodified)
                : (DateTimeOffset?)null;
            var remoteSize = file.filesize >= 0 ? file.filesize : (long?)null;

            var status = DocumentLibraryStatus.Missing;
            string? localPath = null;
            string? checksum = null;
            long? localSize = null;
            DateTimeOffset? lastModified = null;
            var isMoved = false;
            var ambiguousLocation = false;

            if (file.filesize < 0)
            {
                status = DocumentLibraryStatus.Unavailable;
                warnings.Add($"Metadati non validi per «{fileName}»: dimensione dichiarata negativa.");
            }
            else if (DocumentSyncService.IsBlockedFileName(fileName) ||
                     file.filesize > MoodleClient.MaximumDownloadBytes)
            {
                status = DocumentLibraryStatus.Unsupported;
            }
            else if (!LocalFileSecurity.IsInside(courseRoot, expectedPath))
            {
                status = DocumentLibraryStatus.UnsafePath;
                warnings.Add($"Percorso locale non sicuro per «{fileName}»: il materiale non è stato aperto né verificato.");
            }
            else
            {
                if (File.Exists(expectedPath))
                {
                    localPath = expectedPath;
                }
                else if (localFiles.ByName.TryGetValue(fileName, out var matches))
                {
                    var alternatives = matches.Where(path => !PathComparer.Equals(path, expectedPath)).ToArray();
                    if (localFiles.Complete && alternatives.Length == 1)
                    {
                        localPath = alternatives[0];
                        isMoved = true;
                    }
                    else if (alternatives.Length > 0)
                    {
                        ambiguousLocation = true;
                    }
                }

                if (localPath is null)
                {
                    status = DocumentLibraryStatus.Missing;
                }
                else if (!LocalFileSecurity.IsInside(courseRoot, localPath))
                {
                    status = DocumentLibraryStatus.UnsafePath;
                    localPath = null;
                    warnings.Add($"Percorso locale non sicuro per «{fileName}»: il materiale non è stato aperto né verificato.");
                }
                else
                {
                    try
                    {
                        var info = new FileInfo(localPath);
                        info.Refresh();
                        if (!info.Exists)
                        {
                            status = DocumentLibraryStatus.Missing;
                            localPath = null;
                        }
                        else if (info.Length > MoodleClient.MaximumDownloadBytes)
                        {
                            status = DocumentLibraryStatus.Unsupported;
                            localSize = info.Length;
                            lastModified = new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc));
                        }
                        else
                        {
                            localSize = info.Length;
                            lastModified = new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc));
                            var updateRequired =
                                (file.filesize > 0 && info.Length != file.filesize) ||
                                (remoteModified.HasValue && info.LastWriteTimeUtc < remoteModified.Value.UtcDateTime.AddSeconds(-2));
                            status = updateRequired
                                ? DocumentLibraryStatus.UpdateRequired
                                : isMoved ? DocumentLibraryStatus.Moved : DocumentLibraryStatus.Available;

                            if (info.Length <= MaximumHashBytesPerCourse - bytesHashed)
                            {
                                checksum = await ComputeSha256Async(courseRoot, localPath, info, cancellationToken)
                                    .ConfigureAwait(false);
                                bytesHashed += info.Length;
                            }
                            else if (!hashBudgetWarningAdded)
                            {
                                warnings.Add($"Calcolo SHA-256 interrotto dopo il limite di {MaximumHashBytesPerCourse / (1024 * 1024)} MiB per corso; i metadati restano disponibili.");
                                hashBudgetWarningAdded = true;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                                   or ArgumentException or NotSupportedException)
                    {
                        status = DocumentLibraryStatus.Unavailable;
                        localPath = null;
                        localSize = null;
                        lastModified = null;
                        checksum = null;
                        warnings.Add($"Impossibile leggere o verificare «{fileName}»; nessun contenuto è stato modificato.");
                    }
                }
            }

            entries.Add(new DocumentLibraryEntry(
                course.Id,
                course.Name ?? string.Empty,
                source.SectionName,
                source.ModuleName,
                category,
                fileName,
                Path.GetExtension(fileName),
                remoteSize,
                localSize,
                remoteModified,
                lastModified,
                checksum,
                expectedPath,
                localPath,
                status,
                isMoved,
                ambiguousLocation));
        }

        var ordered = entries
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DocumentLibraryIndexResult(
            ordered,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            sourceSet.Truncated,
            localFiles.Complete,
            bytesHashed);
    }

    /// <summary>Applies local search and metadata filters without rescanning or hashing files.</summary>
    public static IReadOnlyList<DocumentLibraryEntry> Filter(
        IEnumerable<DocumentLibraryEntry> entries,
        DocumentLibraryFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (filter is null)
            return Sort(entries);

        if (filter.ModifiedFromUtc.HasValue && filter.ModifiedThroughUtc.HasValue &&
            filter.ModifiedFromUtc.Value > filter.ModifiedThroughUtc.Value)
            throw new ArgumentException("The modified-date range is inverted.", nameof(filter));

        var query = filter.SearchText?.Trim();
        if (query?.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(filter), "Search text cannot exceed 200 characters.");
        var courseId = filter.CourseId?.Trim();
        var category = filter.Category?.Trim();
        var extension = filter.Extension?.Trim();
        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;
        if (extension?.Length > 16)
            throw new ArgumentOutOfRangeException(nameof(filter), "File extension filter is too long.");

        var filtered = entries.Where(item =>
            (string.IsNullOrEmpty(query) ||
             Contains(item.FileName, query) ||
             Contains(item.CourseName, query) ||
             Contains(item.SectionName, query) ||
             Contains(item.ModuleName, query) ||
             Contains(item.Category, query)) &&
            (string.IsNullOrEmpty(courseId) || string.Equals(item.CourseId, courseId, StringComparison.Ordinal)) &&
            (string.IsNullOrEmpty(category) || string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(extension) || string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase)) &&
            (!filter.Status.HasValue || item.Status == filter.Status.Value) &&
            (!filter.ModifiedFromUtc.HasValue ||
             (item.LastModifiedUtc.HasValue && item.LastModifiedUtc.Value >= filter.ModifiedFromUtc.Value)) &&
            (!filter.ModifiedThroughUtc.HasValue ||
             (item.LastModifiedUtc.HasValue && item.LastModifiedUtc.Value <= filter.ModifiedThroughUtc.Value)));

        return Sort(filtered);
    }

    private static IReadOnlyList<DocumentLibraryEntry> Sort(IEnumerable<DocumentLibraryEntry> entries) =>
        entries
            .OrderBy(item => item.CourseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ExpectedPath, PathComparer)
            .ToArray();

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static LocalFileLookup ScanLocalFiles(
        string courseRoot,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(courseRoot))
            return new LocalFileLookup(byName, Complete: true);
        if (!LocalFileSecurity.IsInside(courseRoot, Path.Combine(courseRoot, ".document-library-scan")))
        {
            warnings.Add("La cartella del corso contiene un collegamento o un percorso non verificabile; la scansione locale è stata interrotta.");
            return new LocalFileLookup(byName, Complete: false);
        }

        var pending = new Stack<string>();
        pending.Push(courseRoot);
        var visited = 0;
        var complete = true;
        var unsafeEntries = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visited++;
                if (visited > MaximumLocalEntriesToScan)
                {
                    complete = false;
                    pending.Clear();
                    break;
                }

                if (!LocalFileSecurity.IsInside(courseRoot, child))
                {
                    unsafeEntries++;
                    continue;
                }

                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        unsafeEntries++;
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(child);
                        continue;
                    }

                    var name = Path.GetFileName(child);
                    if (!byName.TryGetValue(name, out var paths))
                    {
                        paths = new List<string>();
                        byName.Add(name, paths);
                    }

                    paths.Add(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    complete = false;
                }
            }
        }

        if (!complete)
            warnings.Add($"La scansione dei file locali è incompleta (limite {MaximumLocalEntriesToScan} elementi o accesso negato); lo stato dei materiali mancanti potrebbe essere provvisorio.");
        if (unsafeEntries > 0)
            warnings.Add($"{unsafeEntries} percorsi locali collegati o non verificabili sono stati esclusi dalla ricerca.");

        return new LocalFileLookup(byName, complete);
    }

    private static async Task<string> ComputeSha256Async(
        string courseRoot,
        string path,
        FileInfo before,
        CancellationToken cancellationToken)
    {
        if (!LocalFileSecurity.IsInside(courseRoot, path))
            throw new InvalidDataException("The document path is no longer inside its course folder.");

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != before.Length)
            throw new IOException("The document changed before hashing.");

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var after = new FileInfo(path);
        after.Refresh();
        if (!LocalFileSecurity.IsInside(courseRoot, path) ||
            after.Length != before.Length ||
            after.LastWriteTimeUtc != before.LastWriteTimeUtc)
            throw new IOException("The document changed while hashing.");

        return Convert.ToHexString(hash);
    }

    private sealed record LocalFileLookup(
        IReadOnlyDictionary<string, List<string>> ByName,
        bool Complete);
}
