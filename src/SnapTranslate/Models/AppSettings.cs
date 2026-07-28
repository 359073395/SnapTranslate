namespace SnapTranslate.Models;

public sealed class AppSettings
{
    public string OcrLanguage { get; set; } = "zh-Hans";
    public string TargetLanguage { get; set; } = "zh-CN";
    public string TranslationProvider { get; set; } = "GoogleWeb";
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public string HotkeyKey { get; set; } = "A";
    public bool HotkeyControl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; }
    public bool HotkeyWindows { get; set; }

    public static IReadOnlyList<LanguageOption> TargetLanguages { get; } =
    [
        new("简体中文", "zh-CN"),
        new("繁体中文", "zh-TW"),
        new("English", "en"),
        new("日本語", "ja"),
        new("한국어", "ko"),
        new("Bahasa Indonesia", "id"),
        new("Français", "fr"),
        new("Deutsch", "de"),
        new("Español", "es")
    ];
}

public sealed record LanguageOption(string Name, string Code);
