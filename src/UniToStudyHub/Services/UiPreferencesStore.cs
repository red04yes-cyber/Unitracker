using System.IO;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;

namespace UniToStudyHub.Services;

/// <summary>
/// Stores non-sensitive presentation preferences beside the user's local app data.
/// This file is not a second study-data store and contains no credentials or course content.
/// </summary>
public sealed class UiPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public UiPreferencesStore()
    {
        _filePath = Path.Combine(StudyHubDatabase.GetDataDirectory(), "ui-preferences.json");
    }

    public UiPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new UiPreferences();

            var preferences = JsonSerializer.Deserialize<UiPreferences>(
                File.ReadAllText(_filePath), JsonOptions) ?? new UiPreferences();

            if (!string.Equals(preferences.Theme, "Light", StringComparison.OrdinalIgnoreCase))
                preferences.Theme = "Dark";
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiPreferences();
        }
    }

    public void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class UiPreferences
{
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsMaximized { get; set; }
    public bool SidebarPinned { get; set; }
    public string Theme { get; set; } = "Dark";
}
