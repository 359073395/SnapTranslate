using System.IO;
using System.Text.Json;
using SnapTranslate.Models;

namespace SnapTranslate.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapTranslate",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(SettingsPath),
                        JsonOptions)
                    ?? new AppSettings();
                settings.OpenAiApiKey =
                    ApiKeyProtector.Unprotect(settings.OpenAiApiKeyProtected);
                return settings;
            }
        }
        catch
        {
            // A malformed file should not prevent the screenshot tool from starting.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        settings.OpenAiApiKeyProtected =
            ApiKeyProtector.Protect(settings.OpenAiApiKey);

        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
