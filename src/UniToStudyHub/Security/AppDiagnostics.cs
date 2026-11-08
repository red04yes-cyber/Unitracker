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
            return File.Exists(path) ? path : null;
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
                LocalFileSecurity.EnsurePrivateDirectory(_directory);
                var path = Path.Combine(_directory, FileName);
                RotateIfNeeded(path);
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

    /// <summary>Deletes the local diagnostics, used by the "delete my data" flow.</summary>
    public static int Clear()
    {
        lock (Gate)
        {
            var deleted = 0;
            try
            {
                if (!System.IO.Directory.Exists(_directory)) return 0;
                foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "unitracker*.log"))
                {
                    LocalFileSecurity.ScrubAndDelete(file);
                    if (!File.Exists(file)) deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Reported as a partial result.
            }
            return deleted;
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.Length < MaximumFileBytes) return;
        for (var index = RetainedRotations; index >= 1; index--)
        {
            var source = index == 1 ? path : path + "." + (index - 1);
            var destination = path + "." + index;
            if (!File.Exists(source)) continue;
            if (File.Exists(destination)) LocalFileSecurity.TryDelete(destination);
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
