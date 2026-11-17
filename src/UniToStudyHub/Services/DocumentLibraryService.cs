using System.IO;
using System.Security.Cryptography;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public enum DocumentFileAvailability
{
    Available,
    Missing,
    Inaccessible,
    UnsafePath
}

public enum DocumentChecksumStatus
{
    Computed,
    FileTooLarge,
    ScanBudgetReached,
    ReadFailed
}

public sealed record DocumentLibraryEntry(
    string CourseName,
    string Category,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string RelativePath,
    string? Sha256,
    DocumentFileAvailability Availability,
    DocumentChecksumStatus ChecksumStatus);

public sealed record DocumentLibraryFilter(
    string? Query = null,
    string? CourseName = null,
    string? Category = null,
    string? Extension = null,
    DocumentFileAvailability? Availability = null,
    DateTimeOffset? ModifiedFromUtc = null,
    DateTimeOffset? ModifiedToUtc = null);

public sealed record DocumentLibraryScanResult(
    IReadOnlyList<DocumentLibraryEntry> Entries,
    int DirectoriesVisited,
    int FilesVisited,
    bool IsTruncated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds an on-demand, read-only index of Moodle files already downloaded
/// under Documents/UniToStudyHub/Moodle. It does not persist a second catalogue,
/// follow links, modify files, or contact Moodle. File hashing and traversal are
/// bounded so an unusually large local folder cannot monopolise the app.
/// </summary>
public sealed class DocumentLibraryService
{
    public const long MaximumFileBytes = 250L * 1024 * 1024;
    public const long MaximumChecksumBytesPerScan = 512L * 1024 * 1024;
    public const int MaximumFilesPerScan = 5_000;
    public const int MaximumDirectoriesPerScan = 5_000;
    public const int MaximumDepth = 16;
    public const int MaximumFilterItems = 10_000;

    private const int HashBufferSize = 64 * 1024;

    public async Task<DocumentLibraryScanResult> ScanAsync(
        string? rootDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullRoot = Path.GetFullPath(rootDirectory ?? GetDefaultRootDirectory());

        if (!Directory.Exists(fullRoot))
            return EmptyResult();

        if (IsReparsePoint(fullRoot))
        {
            return new DocumentLibraryScanResult(
                Array.Empty<DocumentLibraryEntry>(), 0, 0, false,
                new[] { "Archivio non indicizzabile: la cartella principale è un collegamento o non è verificabile." });
        }

        // Directory enumeration and hashing run away from the WPF dispatcher.
        return await Task.Run(
            () => ScanCoreAsync(fullRoot, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<DocumentLibraryEntry> Filter(
        IEnumerable<DocumentLibraryEntry> entries,
        DocumentLibraryFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        filter ??= new DocumentLibraryFilter();

        var query = filter.Query?.Trim();
        var course = filter.CourseName?.Trim();
        var category = filter.Category?.Trim();
        var extension = NormalizeExtension(filter.Extension);
        var result = new List<DocumentLibraryEntry>();

        foreach (var entry in entries.Take(MaximumFilterItems))
        {
            if (entry is null)
                continue;

            if (!string.IsNullOrWhiteSpace(query) &&
                !Contains(entry.FileName, query) &&
                !Contains(entry.CourseName, query) &&
                !Contains(entry.Category, query) &&
                !Contains(entry.RelativePath, query))
                continue;

            if (!string.IsNullOrWhiteSpace(course) &&
                !string.Equals(entry.CourseName, course, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(category) &&
                !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(extension) &&
                !string.Equals(entry.Extension, extension, StringComparison.OrdinalIgnoreCase))
                continue;

            if (filter.Availability is { } availability && entry.Availability != availability)
                continue;

            if (filter.ModifiedFromUtc is { } from && entry.LastModifiedUtc < from)
                continue;

            if (filter.ModifiedToUtc is { } to && entry.LastModifiedUtc > to)
                continue;

            result.Add(entry);
        }

        return result
            .OrderByDescending(entry => entry.LastModifiedUtc)
            .ThenBy(entry => entry.CourseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumFilesPerScan)
            .ToArray();
    }

    /// <summary>
    /// Rechecks a previously displayed entry without trusting its stored path.
    /// This lets a caller mark files moved or deleted since the last scan.
    /// </summary>
    public static DocumentLibraryEntry RefreshAvailability(
        string rootDirectory,
        DocumentLibraryEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(entry);

        if (!Directory.Exists(rootDirectory))
            return entry with { Availability = DocumentFileAvailability.Missing };

        if (!TryResolveSafePath(rootDirectory, entry.RelativePath, out var fullPath))
            return entry with { Availability = DocumentFileAvailability.UnsafePath };

        try
        {
            var info = new FileInfo(fullPath);
            info.Refresh();
            if (!info.Exists)
                return entry with { Availability = DocumentFileAvailability.Missing };

            if (IsReparsePoint(fullPath) || !LocalFileSecurity.IsInside(Path.GetFullPath(rootDirectory), fullPath))
                return entry with { Availability = DocumentFileAvailability.UnsafePath };

            return entry with
            {
                SizeBytes = info.Length,
                LastModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                Availability = DocumentFileAvailability.Available
            };
        }
        catch (FileNotFoundException)
        {
            return entry with { Availability = DocumentFileAvailability.Missing };
        }
        catch (DirectoryNotFoundException)
        {
            return entry with { Availability = DocumentFileAvailability.Missing };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return entry with { Availability = DocumentFileAvailability.Inaccessible };
        }
    }

    /// <summary>
    /// Resolves a path only after rechecking containment, reparse points and
    /// current existence. The caller remains responsible for asking the user
    /// before opening the file in another application.
    /// </summary>
    public static bool TryGetSafePath(
        string rootDirectory,
        DocumentLibraryEntry entry,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(entry);
        fullPath = string.Empty;

        var current = RefreshAvailability(rootDirectory, entry);
        if (current.Availability != DocumentFileAvailability.Available ||
            !TryResolveSafePath(rootDirectory, entry.RelativePath, out var resolved))
            return false;

        fullPath = resolved;
        return true;
    }

    public static string GetDefaultRootDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            throw new InvalidOperationException("La cartella Documenti dell'utente non è disponibile.");

        documents = Path.GetFullPath(documents);
        var documentsInfo = new DirectoryInfo(documents);
        if (documentsInfo.LinkTarget is not null ||
            (Directory.Exists(documents) &&
             (File.GetAttributes(documents) & FileAttributes.ReparsePoint) != 0))
        {
            var resolved = documentsInfo.ResolveLinkTarget(returnFinalTarget: true)
                           ?? throw new InvalidDataException("La cartella Documenti reindirizzata non può essere risolta.");
            documents = Path.GetFullPath(resolved.FullName);
        }

        var root = Path.GetFullPath(Path.Combine(documents, "UniToStudyHub", "Moodle"));
        if (!LocalFileSecurity.IsInside(documents, root))
            throw new InvalidDataException("L'archivio Moodle non è contenuto nella cartella Documenti.");
        return root;
    }

    private static async Task<DocumentLibraryScanResult> ScanCoreAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var entries = new List<DocumentLibraryEntry>();
        var warnings = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        var seenPaths = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        pending.Push((root, 0));

        var directoriesVisited = 0;
        var filesVisited = 0;
        long checksumBytes = 0;
        var truncated = false;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();

            if (!IsSafeDirectory(root, directory))
            {
                warnings.Add("Una cartella collegata o non verificabile è stata esclusa.");
                continue;
            }

            if (directoriesVisited >= MaximumDirectoriesPerScan)
            {
                truncated = true;
                warnings.Add($"Indicizzazione interrotta al limite di {MaximumDirectoriesPerScan} cartelle.");
                break;
            }

            directoriesVisited++;

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (filesVisited >= MaximumFilesPerScan)
                    {
                        truncated = true;
                        warnings.Add($"Indicizzazione interrotta al limite di {MaximumFilesPerScan} file.");
                        break;
                    }

                    filesVisited++;
                    if (!seenPaths.Add(Path.GetFullPath(filePath)))
                        continue;

                    try
                    {
                        var created = await CreateEntryAsync(root, filePath, checksumBytes, cancellationToken)
                            .ConfigureAwait(false);
                        if (created is null)
                            continue;

                        checksumBytes += created.ChecksumBytesRead;
                        entries.Add(created.Entry);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or System.Security.SecurityException or ArgumentException
                                               or NotSupportedException)
                    {
                        warnings.Add("Un file non è leggibile e non è stato indicizzato.");
                    }
                }

                if (truncated)
                    break;

                if (depth >= MaximumDepth)
                {
                    if (Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Any())
                    {
                        truncated = true;
                        warnings.Add($"La profondità dell'archivio supera il limite di {MaximumDepth} livelli.");
                    }

                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsSafeDirectory(root, child))
                    {
                        warnings.Add("Una sottocartella collegata o non verificabile è stata esclusa.");
                        continue;
                    }

                    // Bound queued directories as well as visited directories:
                    // a single folder with millions of children must not grow
                    // the traversal stack without limit.
                    if (directoriesVisited + pending.Count >= MaximumDirectoriesPerScan)
                    {
                        truncated = true;
                        warnings.Add($"Indicizzazione interrotta al limite di {MaximumDirectoriesPerScan} cartelle.");
                        break;
                    }

                    pending.Push((child, depth + 1));
                }

                if (truncated)
                    break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                warnings.Add("Una cartella non è leggibile e non è stata indicizzata.");
            }
        }

        var ordered = entries
            .OrderByDescending(entry => entry.LastModifiedUtc)
            .ThenBy(entry => entry.CourseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DocumentLibraryScanResult(
            ordered, directoriesVisited, filesVisited, truncated, warnings.Distinct().ToArray());
    }

    private static async Task<CreatedEntry?> CreateEntryAsync(
        string root,
        string filePath,
        long checksumBytes,
        CancellationToken cancellationToken)
    {
        if (!LocalFileSecurity.IsInside(root, filePath) || IsReparsePoint(filePath))
            return null;

        var info = new FileInfo(filePath);
        info.Refresh();
        if (!info.Exists || IsTemporaryFile(info.Extension))
            return null;

        var relativePath = Path.GetRelativePath(root, info.FullName);
        if (!IsSafeRelativePath(relativePath))
            return null;

        var size = info.Length;
        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var courseName = segments.Length > 1 ? segments[0] : "Non classificato";
        var category = DocumentSyncService.Categorize(info.Name, string.Empty);

        string? checksum = null;
        var checksumStatus = DocumentChecksumStatus.Computed;
        var availability = DocumentFileAvailability.Available;
        long checksumBytesRead = 0;

        if (size > MaximumFileBytes)
        {
            checksumStatus = DocumentChecksumStatus.FileTooLarge;
        }
        else
        {
            var remainingBudget = MaximumChecksumBytesPerScan - checksumBytes;
            if (size > remainingBudget)
            {
                checksumStatus = DocumentChecksumStatus.ScanBudgetReached;
            }
            else
            {
                try
                {
                    var hashResult = await ComputeChecksumAsync(filePath, remainingBudget, cancellationToken)
                        .ConfigureAwait(false);
                    checksumBytesRead = hashResult.BytesRead;
                    if (hashResult.ExceededLimit)
                    {
                        checksumStatus = DocumentChecksumStatus.ScanBudgetReached;
                    }
                    else if (hashResult.Checksum is null)
                    {
                        checksumStatus = DocumentChecksumStatus.ReadFailed;
                        availability = DocumentFileAvailability.Inaccessible;
                    }
                    else
                    {
                        checksum = hashResult.Checksum;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    checksumStatus = DocumentChecksumStatus.ReadFailed;
                    availability = DocumentFileAvailability.Inaccessible;
                }
            }
        }

        if (availability == DocumentFileAvailability.Available)
        {
            if (IsReparsePoint(filePath) || !LocalFileSecurity.IsInside(root, filePath))
                availability = DocumentFileAvailability.UnsafePath;
            else if (!File.Exists(filePath))
                availability = DocumentFileAvailability.Missing;
        }

        return new CreatedEntry(
            new DocumentLibraryEntry(
                courseName,
                category,
                info.Name,
                info.Extension,
                size,
                modified,
                relativePath,
                checksum,
                availability,
                checksumStatus),
            checksumBytesRead);
    }

    private static async Task<ChecksumResult> ComputeChecksumAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes < 0)
            return new ChecksumResult(null, 0, true);

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            HashBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[HashBufferSize];
        long bytesRead = 0;

        if (stream.Length > maximumBytes)
            return new ChecksumResult(null, 0, true);

        while (bytesRead < maximumBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = maximumBytes - bytesRead;
            var requested = (int)Math.Min(buffer.Length, remaining);
            var count = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                break;

            hash.AppendData(buffer, 0, count);
            bytesRead += count;
        }

        if (stream.Length > bytesRead)
            return new ChecksumResult(null, bytesRead, true);

        return new ChecksumResult(Convert.ToHexString(hash.GetHashAndReset()), bytesRead, false);
    }

    private static bool TryResolveSafePath(string rootDirectory, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsSafeRelativePath(relativePath))
            return false;

        try
        {
            var root = Path.GetFullPath(rootDirectory);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!Directory.Exists(root) || IsReparsePoint(root) ||
                !LocalFileSecurity.IsInside(root, candidate) || IsReparsePoint(candidate))
                return false;

            fullPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsSafeDirectory(string root, string directory)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullDirectory = Path.GetFullPath(directory);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(fullRoot, fullDirectory, comparison))
                return !IsReparsePoint(fullDirectory);

            return !IsReparsePoint(fullDirectory) && LocalFileSecurity.IsInside(fullRoot, fullDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                   or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 &&
               segments.All(segment => segment is not "." and not ".." &&
                                       !segment.Contains(':') &&
                                       !segment.Contains('\0'));
    }

    private static bool IsTemporaryFile(string extension) =>
        extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".part", StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
                return true;

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException or ArgumentException
                                   or NotSupportedException)
        {
            return true;
        }
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeExtension(string? extension)
    {
        var value = extension?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        return value.StartsWith(".", StringComparison.Ordinal) ? value : "." + value;
    }

    private static DocumentLibraryScanResult EmptyResult() =>
        new(Array.Empty<DocumentLibraryEntry>(), 0, 0, false, Array.Empty<string>());

    private sealed record CreatedEntry(DocumentLibraryEntry Entry, long ChecksumBytesRead);
    private sealed record ChecksumResult(string? Checksum, long BytesRead, bool ExceededLimit);
}
