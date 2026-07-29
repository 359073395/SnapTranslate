using System.Text.Json.Serialization;

namespace SnapTranslate.Models;

public sealed class AppSettings
{
    public string OcrLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "zh-CN";
    public string TranslationProvider { get; set; } = "GoogleWeb";
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com/v1";
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public string OpenAiApiKeyProtected { get; set; } = string.Empty;

    [JsonIgnore]
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string HotkeyKey { get; set; } = "A";
    public bool HotkeyControl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; }
    public bool HotkeyWindows { get; set; }

    public static IReadOnlyList<LanguageOption> TargetLanguages { get; } =
    [
        new("简体中文", "zh-CN"),
        new("繁体中文", "zh-TW"),
        new("印尼语 / Bahasa Indonesia", "id"),
        new("English", "en"),
        new("日本語", "ja"),
        new("한국어", "ko"),
        new("Français", "fr"),
        new("Deutsch", "de"),
        new("Español", "es")
    ];
}

public sealed record LanguageOption(string Name, string Code);
