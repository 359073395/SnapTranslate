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
using SnapTranslate.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnapTranslate.Services;

public static class OcrService
{
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
                return languages;
            }
        }
        catch
        {
            // Windows OCR is unavailable on unsupported Windows editions.
        }

        return [new LanguageOption("English", "en")];
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

            Language language = new(languageTag);
            if (!OcrEngine.IsLanguageSupported(language))
            {
                throw new InvalidOperationException(
                    $"Windows 尚未安装 {language.DisplayName} OCR 语言包。");
            }

            OcrEngine engine = OcrEngine.TryCreateFromLanguage(language)
                               ?? throw new InvalidOperationException("无法启动 Windows OCR 引擎。");

            using MemoryStream encoded = new();
            copy.Save(encoded, ImageFormat.Bmp);
            using InMemoryRandomAccessStream stream = new();
            using (IOutputStream output = stream.GetOutputStreamAt(0))
            using (DataWriter writer = new(output))
            {
                writer.WriteBytes(encoded.ToArray());
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
            return new OcrRecognitionResult(recognizedLines);
        }, cancellationToken);
    }
}
