using System.IO;
using System.Security;

namespace UniToStudyHub.Security;

public enum DiagnosticLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Local diagnostics only. There is no telemetry, no remote sink and no
/// background upload: the log stays under the application data folder with a
/// per-user ACL, contains redacted messages, and is rotated so it cannot grow
/// without bound. Callers must never pass document text, prompts, notes or
/// credentials; <see cref="SecurityRedaction"/> is a second line of defence.
/// </summary>
public static class AppDiagnostics
{
    private const long MaximumFileBytes = 512 * 1024;
    private const int RetainedRotations = 2;
    private const string FileName = "unitracker.log";
    private static readonly string[] ManagedLogFileNames =
    {
        FileName,
        FileName + ".1",
        FileName + ".2"
    };
    private static readonly object Gate = new();
    private static string _directory = DefaultDirectory;

    public static bool IsEnabled { get; set; } = true;

    public static string Directory => _directory;

    private static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniToStudyHub", "logs");

    public static void Configure(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        _directory = Path.GetFullPath(directory);
    }

    public static string? CurrentLogPath
    {
        get
        {
            var path = Path.Combine(_directory, FileName);
            return File.Exists(path) && LocalFileSecurity.IsInside(_directory, path) ? path : null;
        }
    }

    /// <summary>
    /// Returns only the current log and the two files managed by rotation.
    /// Reparse points and paths whose containment cannot be verified are
    /// excluded so callers never read or scrub a link target outside the log
    /// directory.
    /// </summary>
    public static IReadOnlyList<string> GetManagedLogFiles()
    {
        try
        {
            var root = Path.GetFullPath(_directory);
            if (!System.IO.Directory.Exists(root)) return Array.Empty<string>();

            var files = new List<string>();
            foreach (var name in ManagedLogFileNames.Reverse())
            {
                var path = Path.Combine(root, name);
                if (!File.Exists(path) || !LocalFileSecurity.IsInside(root, path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                files.Add(path);
            }

            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                       or ArgumentException or NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }

    public static void Info(string category, string message) =>
        Write(DiagnosticLevel.Info, category, message);

    public static void Warning(string category, string message, Exception? exception = null) =>
        Write(DiagnosticLevel.Warning, category, message, exception);

    public static void Error(string category, string message, Exception? exception = null) =>
        Write(DiagnosticLevel.Error, category, message, exception);

    public static void Write(DiagnosticLevel level, string category, string message, Exception? exception = null)
    {
        if (!IsEnabled) return;
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(_directory, FileName);
                // Check before creating/restricting the directory: if the
                // configured root is a link, even ACL changes could affect an
                // unrelated target outside the application's data folder.
                if (!LocalFileSecurity.IsInside(_directory, path)) return;
                LocalFileSecurity.EnsurePrivateDirectory(_directory);
                if (!LocalFileSecurity.IsInside(_directory, path)) return;

                RotateIfNeeded(path);
                if (!LocalFileSecurity.IsInside(_directory, path)) return;

                var line = string.Join(" | ",
                    DateTimeOffset.UtcNow.ToString("O"),
                    level.ToString().ToUpperInvariant(),
                    SanitizeCategory(category),
                    SecurityRedaction.ForLog(message),
                    exception is null ? string.Empty : SecurityRedaction.ExceptionForLog(exception));
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.WriteThrough);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line.TrimEnd(' ', '|'));
                writer.Flush();
                stream.Flush(flushToDisk: true);
                LocalFileSecurity.TryRestrictFile(path);
            }
        }
        catch (Exception)
        {
            // Diagnostics are strictly best-effort. They must never replace the
            // original application exception with a second failure.
        }
    }

    /// <summary>Deletes the current and rotated local diagnostics safely.</summary>
    public static int Clear()
    {
        lock (Gate)
        {
            var deleted = 0;
            foreach (var file in GetManagedLogFiles())
            {
                // Revalidate immediately before opening a file for scrubbing.
                if (!LocalFileSecurity.IsInside(_directory, file)) continue;
                LocalFileSecurity.ScrubAndDelete(file);
                if (!File.Exists(file)) deleted++;
            }
            return deleted;
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || !LocalFileSecurity.IsInside(_directory, path)) return;
        var info = new FileInfo(path);
        if (info.Length < MaximumFileBytes) return;
        for (var index = RetainedRotations; index >= 1; index--)
        {
            var source = index == 1 ? path : path + "." + (index - 1);
            var destination = path + "." + index;
            if (!LocalFileSecurity.IsInside(_directory, source) ||
                !LocalFileSecurity.IsInside(_directory, destination))
                continue;
            if (!File.Exists(source)) continue;
            if (File.Exists(destination)) LocalFileSecurity.TryDelete(destination);
            if (!LocalFileSecurity.IsInside(_directory, destination)) continue;
            File.Move(source, destination, overwrite: true);
        }
    }

    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "app";
        var filtered = new string(category
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-').ToArray());
        return filtered.Length == 0 ? "app" : filtered[..Math.Min(filtered.Length, 40)];
    }
}
