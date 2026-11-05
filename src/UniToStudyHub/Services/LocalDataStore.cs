using System.IO;
using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Legacy JSON store kept for the one-time migration only. SQLite is the single
/// source of truth; this class stays because older builds may still hold
/// <c>data.json</c>, and any remaining copy of personal data must be protected.
/// </summary>
public sealed class LocalDataStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataPath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public LocalDataStore(string? dataDirectory = null)
    {
        var folder = dataDirectory ?? StudyHubDatabase.GetDataDirectory();
        LocalFileSecurity.EnsurePrivateDirectory(folder);
        _dataPath = Path.Combine(folder, "data.json");
    }

    public async Task<StudyData> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_dataPath)) return new StudyData();
            await using var stream = File.OpenRead(_dataPath);
            return await JsonSerializer.DeserializeAsync<StudyData>(stream, _options, cancellationToken)
                       .ConfigureAwait(false)
                   ?? new StudyData();
        }
        catch (JsonException)
        {
            var backup = _dataPath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            try { File.Move(_dataPath, backup, overwrite: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppDiagnostics.Warning("legacy", "File JSON legacy non valido e non riposizionabile.", ex);
            }
            return new StudyData();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(StudyData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(data, _options);
            await LocalFileSecurity.WriteAllTextAtomicallyAsync(_dataPath, json, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
