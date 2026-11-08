using System.IO;
using System.Text.Json;

namespace UniToStudyHub.Services;

/// <summary>
/// Persists non-secret AI preferences locally. API keys are deliberately
/// excluded and must be stored separately by AiCredentialStore (DPAPI).
/// </summary>
public sealed class AiProviderPreferencesStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public AiProviderPreferencesStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(Path.GetFullPath(dataDirectory), "ai-provider-preferences.json");
    }

    public AiProviderPreferences? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var json = File.ReadAllText(_path);
                var value = JsonSerializer.Deserialize<AiProviderPreferences>(json);
                return value is { Provider: "openai" or "ollama" or "gemini" } ? value : null;
            }
            catch (IOException) { return null; }
            catch (JsonException) { return null; }
        }
    }

    public void Save(AiProviderPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Provider is not ("openai" or "ollama" or "gemini"))
            throw new ArgumentException("Provider AI non valido.", nameof(preferences));
        if (string.IsNullOrWhiteSpace(preferences.Model) || preferences.Model.Length > 120)
            throw new ArgumentException("Il modello AI è obbligatorio e deve essere lungo al massimo 120 caratteri.", nameof(preferences));
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(preferences);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
    }
}

public sealed record AiProviderPreferences(string Provider, string Model, bool ConsentGranted);
