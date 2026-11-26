using System.IO;
using System.Net.Http;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public sealed record DocumentSyncResult(int Downloaded, int Skipped, IReadOnlyList<string> Warnings);
public sealed record MoodleDocumentSource(string SectionName, string ModuleName, MoodleContentFile File);
public sealed record MoodleDocumentSourceSet(
    IReadOnlyList<MoodleDocumentSource> Files, bool Truncated, int Rejected);

/// <summary>
/// Downloads the files exposed by an authorised Moodle course into the user's
/// documents folder.
///
/// Every destination is rebuilt from a sanitised file name, verified to stay
/// inside the course folder, and never carries an executable extension: a
/// malicious or compromised Moodle item cannot drop a runnable file into a
/// predictable path. Downloads are written atomically by
/// <see cref="MoodleClient"/>; failed items leave no partial file behind.
/// </summary>
public sealed class DocumentSyncService
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msp", ".msix", ".appx", ".bat", ".cmd", ".com", ".cpl", ".scr", ".pif",
        ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".reg", ".lnk",
        ".jar", ".dll", ".sys", ".drv", ".sh", ".bash", ".zsh", ".py", ".rb", ".pl", ".docm", ".xlsm"
    };

    public const int MaximumFilesPerCourse = 1000;
    public const int MaximumFilePathLength = 2048;
    public const int MaximumFilePathSegments = 32;
    private const int MaximumSourceUrlLength = 8192;
    private const int MaximumSourceFileNameLength = 1024;

    /// <summary>
    /// Produces one bounded, deterministic source list shared by download and
    /// library indexing. URL identity is ordinal because Moodle path segments
    /// can be case-sensitive. Malformed descriptors never reach filesystem code.
    /// </summary>
    public static MoodleDocumentSourceSet EnumerateSources(
        IEnumerable<MoodleCourseSection> sections,
        IEnumerable<MoodleContentFile>? additionalFiles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var files = new List<MoodleDocumentSource>(MaximumFilesPerCourse);
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;
        var truncated = false;

        void Add(string? sectionName, string? moduleName, MoodleContentFile? file)
        {
            if (file is null || !string.Equals(file.type, "file", StringComparison.OrdinalIgnoreCase))
                return;

            var filePath = file.filepath ?? string.Empty;
            var segmentCount = filePath.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Length;
            if (string.IsNullOrWhiteSpace(file.fileurl) || file.fileurl.Length > MaximumSourceUrlLength ||
                string.IsNullOrWhiteSpace(file.filename) || file.filename.Length > MaximumSourceFileNameLength ||
                filePath.Length > MaximumFilePathLength || segmentCount > MaximumFilePathSegments)
            {
                rejected++;
                return;
            }

            if (!seenUrls.Add(file.fileurl))
                return;
            if (files.Count >= MaximumFilesPerCourse)
            {
                truncated = true;
                return;
            }

            files.Add(new MoodleDocumentSource(
                sectionName ?? "Sezione Moodle",
                moduleName ?? "Materiali Moodle",
                file));
        }

        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (section?.modules is null)
                continue;

            foreach (var module in section.modules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (module?.contents is null)
                    continue;

                foreach (var file in module.contents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Add(section.name, module.name, file);
                    if (truncated) break;
                }

                if (truncated) break;
            }

            if (truncated) break;
        }

        if (!truncated && additionalFiles is not null)
        {
            foreach (var file in additionalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add("Attività Moodle", "Allegati attività", file);
                if (truncated) break;
            }
        }

        return new MoodleDocumentSourceSet(files, truncated, rejected);
    }

    /// <summary>Returns the same course directory used by Moodle downloads without creating it.</summary>
    public static string GetCourseDirectory(Course course, string? documentsBasePath = null)
    {
        ArgumentNullException.ThrowIfNull(course);
        var basePath = documentsBasePath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException("La cartella Documenti dell'utente non è disponibile.");

        basePath = ResolveDocumentsBaseDirectory(basePath);
        var documentsRoot = Path.Combine(basePath, "UniToStudyHub");
        var moodleRoot = Path.Combine(documentsRoot, "Moodle");
        var courseRoot = Path.Combine(moodleRoot, SafeName(course.Name));

        if (!LocalFileSecurity.IsInside(basePath, documentsRoot) ||
            !LocalFileSecurity.IsInside(documentsRoot, moodleRoot) ||
            !LocalFileSecurity.IsInside(moodleRoot, courseRoot))
            throw new InvalidDataException("La cartella del corso non è contenuta nell'archivio Moodle.");

        return courseRoot;
    }

    /// <summary>Builds the relative path shared by downloads and the local document index.</summary>
    public static string GetRelativePath(MoodleDocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return GetRelativePath(source.SectionName, source.ModuleName, source.File);
    }

    public static string GetRelativePath(string? sectionName, string? moduleName, MoodleContentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var relative = Path.Combine(
            SafeName(sectionName),
            SafeName(moduleName),
            Categorize(file.filename ?? string.Empty, file.mimetype ?? string.Empty));
        var rawFolders = (file.filepath ?? string.Empty).Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if ((file.filepath?.Length ?? 0) > MaximumFilePathLength ||
            rawFolders.Length > MaximumFilePathSegments)
            throw new InvalidDataException("Il percorso relativo del materiale supera i limiti consentiti.");
        var folders = rawFolders
            .Where(part => part is not "." and not "..")
            .Select(SafeName);
        foreach (var folder in folders)
            relative = Path.Combine(relative, folder);
        return relative;
    }

    public static bool IsBlockedFileName(string? fileName) =>
        BlockedExtensions.Contains(Path.GetExtension(SafeName(fileName)));

    private static string ResolveDocumentsBaseDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            throw new InvalidDataException("Il percorso Documenti configurato è un file, non una cartella.");
        var directoryInfo = new DirectoryInfo(fullPath);
        if (directoryInfo.LinkTarget is not null ||
            (Directory.Exists(fullPath) &&
             (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0))
        {
            // The user-selected Documents folder may legitimately be redirected
            // (for example by OneDrive). Resolve that trusted boundary once.
            var resolvedBase = directoryInfo.ResolveLinkTarget(returnFinalTarget: true)
                               ?? throw new InvalidDataException(
                                   "La cartella Documenti reindirizzata non può essere risolta.");
            fullPath = Path.GetFullPath(resolvedBase.FullName);
        }

        return fullPath;
    }

    public async Task<DocumentSyncResult> DownloadCourseFilesAsync(
        MoodleClient client,
        Course course,
        IEnumerable<MoodleCourseSection> sections,
        IEnumerable<MoodleContentFile>? additionalFiles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(sections);

        var documentsBase = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documentsBase))
            throw new InvalidOperationException("La cartella Documenti dell'utente non è disponibile.");

        documentsBase = ResolveDocumentsBaseDirectory(documentsBase);

        var documentsRoot = Path.Combine(documentsBase, "UniToStudyHub");
        var moodleRoot = Path.Combine(documentsRoot, "Moodle");
        var root = GetCourseDirectory(course, documentsBase);

        // Reject links at each application-owned boundary before applying ACLs
        // or creating directories. A link must not redirect a Moodle download
        // into an unrelated user folder.
        if (!LocalFileSecurity.IsInside(documentsBase, documentsRoot))
            throw new InvalidDataException("La cartella dati Moodle non è un percorso locale sicuro.");
        LocalFileSecurity.EnsurePrivateDirectory(documentsRoot);
        if (!LocalFileSecurity.IsInside(documentsBase, documentsRoot) ||
            !LocalFileSecurity.IsInside(documentsRoot, moodleRoot))
            throw new InvalidDataException("La cartella Moodle contiene un collegamento non consentito.");

        LocalFileSecurity.EnsurePrivateDirectory(moodleRoot);
        if (!LocalFileSecurity.IsInside(documentsRoot, moodleRoot) ||
            !LocalFileSecurity.IsInside(moodleRoot, root))
            throw new InvalidDataException("La cartella del corso contiene un collegamento non consentito.");

        LocalFileSecurity.EnsurePrivateDirectory(root);
        if (!LocalFileSecurity.IsInside(moodleRoot, root))
            throw new InvalidDataException("La cartella del corso non è contenuta nell'archivio Moodle.");

        var warnings = new List<string>();
        var downloaded = 0;
        var skipped = 0;
        long downloadedBytes = 0;
        const long maximumCourseBytes = 1024L * 1024 * 1024;
        var sourceSet = EnumerateSources(sections, additionalFiles, cancellationToken);
        var files = sourceSet.Files;
        if (sourceSet.Truncated)
            warnings.Add($"Il corso espone più di {MaximumFilesPerCourse} file: il download è stato limitato per sicurezza.");
        if (sourceSet.Rejected > 0)
        {
            skipped += sourceSet.Rejected;
            warnings.Add("Alcuni allegati Moodle sono stati ignorati perché incompleti o oltre i limiti consentiti.");
        }

        foreach (var item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = item.File;
            var safeName = SafeName(file.filename);
            if (IsBlockedFileName(safeName))
            {
                warnings.Add($"Ignorato «{safeName}»: estensione eseguibile o macro non consentita.");
                skipped++;
                continue;
            }
            if (file.filesize < 0)
            {
                warnings.Add($"Ignorato «{safeName}»: dimensione dichiarata non valida.");
                skipped++;
                continue;
            }
            if (file.filesize > MoodleClient.MaximumDownloadBytes)
            {
                warnings.Add($"Ignorato «{safeName}»: supera il limite di 250 MB.");
                skipped++;
                continue;
            }
            if (file.filesize > 0 && downloadedBytes + file.filesize > maximumCourseBytes)
            {
                warnings.Add("Raggiunto il limite di 1 GB per corso; i file rimanenti non sono stati scaricati.");
                skipped += files.Count - downloaded - skipped;
                break;
            }

            var relative = GetRelativePath(item);
            var destinationDirectory = Path.Combine(root, relative);
            var destination = Path.Combine(destinationDirectory, safeName);
            if (!LocalFileSecurity.IsInside(moodleRoot, destination))
            {
                warnings.Add($"Ignorato «{safeName}»: percorso esterno o collegamento non consentito.");
                skipped++;
                continue;
            }

            if (File.Exists(destination) && file.timemodified > 0)
            {
                var existingTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(destination), TimeSpan.Zero)
                    .ToUnixTimeSeconds();
                if (existingTimestamp >= file.timemodified)
                {
                    skipped++;
                    continue;
                }
            }
            else if (File.Exists(destination))
            {
                skipped++;
                continue;
            }

            try
            {
                if (!LocalFileSecurity.IsInside(moodleRoot, destinationDirectory))
                    throw new InvalidDataException("La destinazione contiene un collegamento non consentito.");

                LocalFileSecurity.EnsurePrivateDirectory(destinationDirectory);
                if (!LocalFileSecurity.IsInside(moodleRoot, destination))
                    throw new InvalidDataException("La destinazione del download non è più sicura.");

                await client.DownloadFileAsync(file.fileurl, destination, cancellationToken, moodleRoot)
                    .ConfigureAwait(false);
                var info = new FileInfo(destination);
                if (downloadedBytes + info.Length > maximumCourseBytes)
                {
                    LocalFileSecurity.ScrubAndDelete(destination);
                    warnings.Add("Raggiunto il limite di 1 GB per corso; il file è stato rimosso.");
                    skipped++;
                    break;
                }
                downloadedBytes += info.Length;
                if (file.timemodified > 0)
                    File.SetLastWriteTimeUtc(destination, DateTimeOffset.FromUnixTimeSeconds(file.timemodified).UtcDateTime);
                downloaded++;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                           or InvalidDataException or TaskCanceledException or ArgumentOutOfRangeException)
            {
                LocalFileSecurity.TryDelete(destination);
                warnings.Add($"«{safeName}» non scaricato: {SecurityRedaction.ForLog(ex.Message, 200)}");
                AppDiagnostics.Warning("moodle", "Download corso non riuscito.", ex);
            }
        }

        return new DocumentSyncResult(downloaded, skipped, warnings);
    }

    public static string Categorize(string name, string mime)
    {
        var candidate = name ?? string.Empty;
        var mediaType = mime ?? string.Empty;
        var extension = Path.GetExtension(candidate).ToLowerInvariant();
        if (candidate.Contains("syllabus", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("programma", StringComparison.OrdinalIgnoreCase)) return "Syllabus";
        if (extension == ".pdf" || mediaType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) return "PDF";
        if (extension is ".ppt" or ".pptx" or ".odp" ||
            mediaType.Contains("presentation", StringComparison.OrdinalIgnoreCase)) return "Slide";
        if (extension is ".csv" or ".xls" or ".xlsx" or ".json" or ".zip" or ".parquet" or ".sav" or ".dta")
            return "Dataset";
        return "Altro";
    }

    /// <summary>
    /// Keeps only the final path segment, replaces reserved characters, strips
    /// control characters, refuses traversal segments and Windows device names.
    /// </summary>
    public static string SafeName(string? value)
    {
        // Normalise both separators before taking the leaf: a Windows-style path
        // imported on another OS must not keep its directory components.
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        var candidate = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(candidate)) return "Senza-nome";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate
            .Select(character => invalid.Contains(character) || char.IsControl(character) ||
                                 character is '/' or '\\' or ':'
                ? '_'
                : character)
            .ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..") return "Senza-nome";
        if (sanitized.Length > 120)
        {
            var extension = Path.GetExtension(sanitized);
            var stem = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = extension.Length is > 0 and <= 16
                ? stem[..Math.Min(stem.Length, 120 - extension.Length)] + extension
                : sanitized[..120];
        }
        var deviceStem = Path.GetFileNameWithoutExtension(sanitized);
        var reserved = new[]
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8",
            "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        return reserved.Contains(deviceStem, StringComparer.OrdinalIgnoreCase) ? "_" + sanitized : sanitized;
    }
}
