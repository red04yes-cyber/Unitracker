using System.IO;
using System.Text.Json;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>Durable, atomic local persistence for calendar items.</summary>
public sealed class CalendarEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniToStudyHub", "calendar-events.json");

    public async Task<List<CalendarEvent>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new List<CalendarEvent>();
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<CalendarEvent>>(stream, JsonOptions, cancellationToken)
            ?? new List<CalendarEvent>();
    }

    public async Task SaveAsync(IEnumerable<CalendarEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, events.ToArray(), JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, _path, overwrite: true);
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
