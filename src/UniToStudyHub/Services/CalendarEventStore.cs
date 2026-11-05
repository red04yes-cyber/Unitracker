using System.IO;
using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Durable, atomic local persistence for the legacy calendar file. New calendar
/// items live in SQLite; this store only supports the one-time migration and is
/// hardened so a leftover file cannot become an unprotected copy of personal
/// entries.
/// </summary>
public sealed class CalendarEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public CalendarEventStore(string? dataDirectory = null)
    {
        var root = dataDirectory ?? StudyHubDatabase.GetDataDirectory();
        LocalFileSecurity.EnsurePrivateDirectory(root);
        _path = Path.Combine(root, "calendar-events.json");
    }

    public async Task<List<CalendarEvent>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new List<CalendarEvent>();
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<CalendarEvent>>(stream, JsonOptions, cancellationToken)
                       .ConfigureAwait(false)
                   ?? new List<CalendarEvent>();
        }
        catch (JsonException)
        {
            // Keep the damaged file as forensic evidence instead of silently
            // replacing it with an empty calendar, and never log its content.
            var parked = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            try { File.Move(_path, parked, overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppDiagnostics.Warning("calendar", "File calendario non valido e non riposizionabile.", ex);
            }
            AppDiagnostics.Warning("calendar", "File calendario locale non valido: contenuto non importato.");
            return new List<CalendarEvent>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Warning("calendar", "File calendario locale non leggibile.", ex);
            return new List<CalendarEvent>();
        }
    }

    public async Task SaveAsync(IEnumerable<CalendarEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var directory = Path.GetDirectoryName(_path)!;
        LocalFileSecurity.EnsurePrivateDirectory(directory);
        await LocalFileSecurity.WriteAllTextAtomicallyAsync(_path,
            JsonSerializer.Serialize(events.ToArray(), JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parks the legacy file after its events have been copied to SQLite.</summary>
    public Task MarkMigratedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path)) return Task.CompletedTask;
        var target = _path + ".migrated";
        File.Move(_path, target, overwrite: true);
        return Task.CompletedTask;
    }
}
