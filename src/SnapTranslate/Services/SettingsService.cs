using System.IO;
using System.Text.Json;
using SnapTranslate.Models;

namespace SnapTranslate.Services;

public sealed class SettingsService
{
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapTranslate",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LingxiCapture",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            string? loadPath = File.Exists(SettingsPath)
                ? SettingsPath
                : File.Exists(LegacySettingsPath)
                    ? LegacySettingsPath
                    : null;
            if (loadPath is not null)
            {
                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(loadPath),
                        JsonOptions)
                    ?? new AppSettings();
                settings.OpenAiApiKey =
                    ApiKeyProtector.Unprotect(settings.OpenAiApiKeyProtected);
                if (!string.Equals(
                        loadPath,
                        SettingsPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Save(settings);
                }

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
