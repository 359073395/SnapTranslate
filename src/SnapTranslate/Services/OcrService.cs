/*
 * OCR flow adapted from ShareX 21.0.0:
 * https://github.com/ShareX/ShareX/blob/v21.0.0/ShareX.Tools/Tools/OCR/OCRHelper.cs
 *
 * ShareX is copyright (c) ShareX Team and licensed under GNU GPL v3.
 * This project is distributed under the same license.
 */

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using SnapTranslate.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnapTranslate.Services;

public static class OcrService
{
    public const string AutoLanguageTag = "auto";
    private const int MaximumAutoCandidates = 8;

    public static IReadOnlyList<LanguageOption> GetAvailableLanguages()
    {
        try
        {
            LanguageOption[] languages = OcrEngine.AvailableRecognizerLanguages
                .Select(language => new LanguageOption(language.DisplayName, language.LanguageTag))
                .OrderBy(language => language.Name)
                .ToArray();

            if (languages.Length > 0)
            {
                return
                [
                    new LanguageOption("自动识别（推荐）", AutoLanguageTag),
                    .. languages
                ];
            }
        }
        catch
        {
            // Windows OCR is unavailable on unsupported Windows editions.
        }

        return
        [
            new LanguageOption("自动识别（推荐）", AutoLanguageTag),
            new LanguageOption("English", "en")
        ];
    }

    public static async Task<string> RecognizeAsync(
        Bitmap bitmap,
        string languageTag,
        CancellationToken cancellationToken = default) =>
        (await RecognizeDetailedAsync(bitmap, languageTag, cancellationToken)).Text;

    public static async Task<OcrRecognitionResult> RecognizeDetailedAsync(
        Bitmap bitmap,
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        using Bitmap copy = new(bitmap);

        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] encodedBytes;
            using (MemoryStream encoded = new())
            {
                copy.Save(encoded, ImageFormat.Bmp);
                encodedBytes = encoded.ToArray();
            }

            if (string.Equals(languageTag, AutoLanguageTag, StringComparison.OrdinalIgnoreCase))
            {
                return await RecognizeAutomaticallyAsync(
                    encodedBytes,
                    copy.Width,
                    copy.Height,
                    cancellationToken);
            }

            Language language = new(languageTag);
            if (!OcrEngine.IsLanguageSupported(language))
            {
                throw new InvalidOperationException(
                    $"Windows 尚未安装 {language.DisplayName} OCR 语言包。");
            }

            return await RecognizeWithLanguageAsync(
                encodedBytes,
                language,
                cancellationToken);
        }, cancellationToken);
    }

    public static string GetRecognizerDescription(string languageTag)
    {
        if (languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "中文";
        }

        if (languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "日文";
        }

        if (languageTag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return "韩文";
        }

        if (languageTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            return "阿拉伯文字";
        }

        if (languageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
            languageTag.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
        {
            return "西里尔文字";
        }

        return "拉丁文字";
    }

    private static async Task<OcrRecognitionResult> RecognizeAutomaticallyAsync(
        byte[] encodedBytes,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken)
    {
        Language[] availableLanguages = OcrEngine.AvailableRecognizerLanguages.ToArray();
        if (availableLanguages.Length == 0)
        {
            throw new InvalidOperationException(
                "Windows 没有可用的 OCR 语言包，请先在系统设置中安装语言包。");
        }

        IReadOnlyList<Language> candidates = BuildAutoCandidates(availableLanguages);
        OcrRecognitionResult? bestResult = null;
        double bestScore = double.NegativeInfinity;

        foreach (Language candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrRecognitionResult result = await RecognizeWithLanguageAsync(
                encodedBytes,
                candidate,
                cancellationToken);
            double score = ScoreRecognition(
                result,
                candidate.LanguageTag,
                imageWidth,
                imageHeight);
            if (score > bestScore)
            {
                bestResult = result;
                bestScore = score;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return bestResult ?? OcrRecognitionResult.Empty;
    }

    private static IReadOnlyList<Language> BuildAutoCandidates(
        IReadOnlyList<Language> availableLanguages)
    {
        List<Language> candidates = [];

        OcrEngine? profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profileEngine is not null)
        {
            AddCandidate(candidates, profileEngine.RecognizerLanguage);
        }

        string[] preferredPrefixes =
        [
            "zh-Hans",
            "zh-Hant",
            "ja",
            "ko",
            "en",
            "id",
            "ru",
            "ar"
        ];

        foreach (string prefix in preferredPrefixes)
        {
            Language? language = availableLanguages.FirstOrDefault(
                item => item.LanguageTag.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));
            if (language is not null)
            {
                AddCandidate(candidates, language);
            }
        }

        foreach (Language language in availableLanguages)
        {
            if (candidates.Count >= MaximumAutoCandidates)
            {
                break;
            }

            AddCandidate(candidates, language);
        }

        return candidates.Take(MaximumAutoCandidates).ToArray();
    }

    private static void AddCandidate(List<Language> candidates, Language language)
    {
        if (candidates.All(
                item => !string.Equals(
                    item.LanguageTag,
                    language.LanguageTag,
                    StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(language);
        }
    }

    private static async Task<OcrRecognitionResult> RecognizeWithLanguageAsync(
        byte[] encodedBytes,
        Language language,
        CancellationToken cancellationToken)
    {
        OcrEngine engine = OcrEngine.TryCreateFromLanguage(language)
                           ?? throw new InvalidOperationException(
                               $"无法启动 {language.DisplayName} Windows OCR 引擎。");

        using InMemoryRandomAccessStream stream = new();
        using (IOutputStream output = stream.GetOutputStreamAt(0))
        using (DataWriter writer = new(output))
        {
            writer.WriteBytes(encodedBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        OcrResult result = await engine.RecognizeAsync(softwareBitmap);

        List<OcrTextLine> recognizedLines = [];
        foreach (OcrLine line in result.Lines)
        {
            OcrWord[] words = line.Words.ToArray();
            if (words.Length == 0)
            {
                continue;
            }

            string text;
            if (language.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                language.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                text = string.Concat(words.Select(word => word.Text));
            }
            else if (language.LayoutDirection == LanguageLayoutDirection.Rtl)
            {
                text = string.Join(" ", words.Reverse().Select(word => word.Text));
            }
            else
            {
                text = line.Text;
            }

            double left = words.Min(word => word.BoundingRect.X);
            double top = words.Min(word => word.BoundingRect.Y);
            double right = words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
            double bottom = words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
            if (!string.IsNullOrWhiteSpace(text) && right > left && bottom > top)
            {
                recognizedLines.Add(
                    new OcrTextLine(
                        text.Trim(),
                        left,
                        top,
                        right - left,
                        bottom - top));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new OcrRecognitionResult(
            recognizedLines,
            engine.RecognizerLanguage.LanguageTag);
    }

    private static double ScoreRecognition(
        OcrRecognitionResult result,
        string languageTag,
        int imageWidth,
        int imageHeight)
    {
        string text = result.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        int latin = 0;
        int cjk = 0;
        int kana = 0;
        int hangul = 0;
        int cyrillic = 0;
        int arabic = 0;
        int digits = 0;
        int replacementCharacters = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (Rune.IsDigit(rune))
            {
                digits++;
            }
            else if (IsCjk(value))
            {
                cjk++;
            }
            else if (IsKana(value))
            {
                kana++;
            }
            else if (IsHangul(value))
            {
                hangul++;
            }
            else if (IsCyrillic(value))
            {
                cyrillic++;
            }
            else if (IsArabic(value))
            {
                arabic++;
            }
            else if (IsLatin(value))
            {
                latin++;
            }
            else if (value == 0xFFFD)
            {
                replacementCharacters++;
            }
        }

        int recognizedCharacters =
            latin + cjk + kana + hangul + cyrillic + arabic + digits;
        double imageArea = Math.Max(1, imageWidth * (double)imageHeight);
        double coveredArea = result.Lines.Sum(
            line => Math.Max(0, line.Width) * Math.Max(0, line.Height));
        double score =
            recognizedCharacters * 1.5 +
            result.Lines.Count * 3 +
            Math.Min(1, coveredArea / imageArea) * 200 -
            replacementCharacters * 8;

        if (languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            score += cjk * 8 + latin * 0.75;
        }
        else if (languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            score += kana * 10 + cjk * 4 + latin * 0.75;
        }
        else if (languageTag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            score += hangul * 10 + latin * 0.75;
        }
        else if (languageTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            score += arabic * 10;
        }
        else if (languageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
                 languageTag.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
        {
            score += cyrillic * 10;
        }
        else
        {
            score += latin * 4 - (cjk + kana + hangul + cyrillic + arabic) * 2;
        }

        return score;
    }

    private static bool IsLatin(int value) =>
        value is >= 0x0041 and <= 0x005A or
            >= 0x0061 and <= 0x007A or
            >= 0x00C0 and <= 0x024F or
            >= 0x1E00 and <= 0x1EFF;

    private static bool IsCjk(int value) =>
        value is >= 0x3400 and <= 0x4DBF or
            >= 0x4E00 and <= 0x9FFF or
            >= 0xF900 and <= 0xFAFF;

    private static bool IsKana(int value) =>
        value is >= 0x3040 and <= 0x30FF or
            >= 0x31F0 and <= 0x31FF;

    private static bool IsHangul(int value) =>
        value is >= 0x1100 and <= 0x11FF or
            >= 0x3130 and <= 0x318F or
            >= 0xAC00 and <= 0xD7AF;

    private static bool IsCyrillic(int value) =>
        value is >= 0x0400 and <= 0x052F;

    private static bool IsArabic(int value) =>
        value is >= 0x0600 and <= 0x06FF or
            >= 0x0750 and <= 0x077F or
            >= 0x08A0 and <= 0x08FF;
}
