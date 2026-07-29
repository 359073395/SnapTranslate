namespace SnapTranslate.Models;

public sealed record OcrTextLine(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record OcrRecognitionResult(
    IReadOnlyList<OcrTextLine> Lines,
    string RecognizerLanguageTag = "")
{
    public static OcrRecognitionResult Empty { get; } = new([]);

    public string Text =>
        string.Join(
            Environment.NewLine,
            Lines.Select(line => line.Text)).Trim();
}
