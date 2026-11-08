using System.IO;
using System.Net.Http;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public sealed record DocumentSyncResult(int Downloaded, int Skipped, IReadOnlyList<string> Warnings);

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

    public async Task<DocumentSyncResult> DownloadCourseFilesAsync(
        MoodleClient client,
        Course course,
        IEnumerable<MoodleCourseSection> sections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(sections);

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "UniToStudyHub",
            SafeName(course.Name));
        LocalFileSecurity.EnsurePrivateDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UniToStudyHub"));
        LocalFileSecurity.EnsurePrivateDirectory(root);

        var warnings = new List<string>();
        var downloaded = 0;
        var skipped = 0;
        long downloadedBytes = 0;
        const long maximumCourseBytes = 1024L * 1024 * 1024;
        const int maximumFilesPerCourse = 1000;
        var files = sections
            .Where(section => section.modules is not null)
            .SelectMany(section => section.modules.SelectMany(module =>
                (module.contents ?? new List<MoodleContentFile>())
                .Select(file => new { Section = section, Module = module, File = file })))
            .Where(item => item.File.type.Equals("file", StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(item.File.fileurl) &&
                           !string.IsNullOrWhiteSpace(item.File.filename))
            .Take(maximumFilesPerCourse + 1)
            .ToList();

        if (files.Count > maximumFilesPerCourse)
        {
            warnings.Add($"Il corso espone più di {maximumFilesPerCourse} file: il download è stato limitato per sicurezza.");
            files.RemoveAt(files.Count - 1);
        }

        foreach (var item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = item.File;
            var safeName = SafeName(file.filename);
            if (BlockedExtensions.Contains(Path.GetExtension(safeName)))
            {
                warnings.Add($"Ignorato «{safeName}»: estensione eseguibile o macro non consentita.");
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

            var relative = Path.Combine(
                SafeName(item.Section.name),
                SafeName(item.Module.name),
                Categorize(file.filename, file.mimetype));
            if (!string.IsNullOrWhiteSpace(file.filepath))
            {
                var folders = file.filepath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => part is not "." and not "..")
                    .Select(SafeName).ToArray();
                foreach (var folder in folders) relative = Path.Combine(relative, folder);
            }
            var destinationDirectory = Path.Combine(root, relative);
            var destination = Path.Combine(destinationDirectory, safeName);
            if (!LocalFileSecurity.IsInside(root, destination))
            {
                warnings.Add($"Ignorato «{safeName}»: percorso non valido.");
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
                LocalFileSecurity.EnsurePrivateDirectory(destinationDirectory);
                await client.DownloadFileAsync(file.fileurl, destination, cancellationToken, root)
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
