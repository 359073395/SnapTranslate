using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SnapTranslate.Models;
using SnapTranslate.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Cursors = System.Windows.Input.Cursors;
using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Size = System.Windows.Size;

namespace SnapTranslate.Views;

public partial class EditorWindow : Window
{
    private enum ToolMode
    {
        Pointer,
        Rectangle,
        Pen,
        Text
    }

    private readonly Bitmap _sourceBitmap;
    private readonly BitmapSource _source;
    private readonly AppSettings _settings;
    private readonly TranslationService _translationService;
    private readonly List<UIElement> _annotations = [];
    private readonly CancellationTokenSource _workCancellation = new();

    private ToolMode _tool = ToolMode.Pointer;
    private Brush _drawingBrush = Brushes.Red;
    private double _drawingThickness = 4;
    private Point _start;
    private Shape? _activeShape;
    private OcrRecognitionResult? _recognition;
    private bool _drawing;
    private bool _busy;

    public EditorWindow(Bitmap bitmap, AppSettings settings)
    {
        InitializeComponent();

        _sourceBitmap = bitmap;
        _source = ScreenCaptureService.ToBitmapSource(bitmap);
        _settings = settings;
        _translationService = new TranslationService(settings);

        CaptureImage.Source = _source;
        DocumentGrid.Width = bitmap.Width;
        DocumentGrid.Height = bitmap.Height;
        CaptureImage.Width = bitmap.Width;
        CaptureImage.Height = bitmap.Height;
        TranslationCanvas.Width = bitmap.Width;
        TranslationCanvas.Height = bitmap.Height;
        AnnotationCanvas.Width = bitmap.Width;
        AnnotationCanvas.Height = bitmap.Height;

        Width = Math.Clamp(bitmap.Width + 90, 760, Math.Max(760, SystemParameters.WorkArea.Width - 50));
        Height = Math.Clamp(bitmap.Height + 180, 520, Math.Max(520, SystemParameters.WorkArea.Height - 50));

        Closed += EditorWindow_Closed;
        SetTool(ToolMode.Pointer, PointerToolButton);
    }

    private void PointerToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Pointer, PointerToolButton);

    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Rectangle, RectangleToolButton);

    private void PenToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Pen, PenToolButton);

    private void TextToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Text, TextToolButton);

    private void SetTool(ToolMode tool, Button selectedButton)
    {
        _tool = tool;
        foreach (Button button in new[]
                 {
                     PointerToolButton,
                     RectangleToolButton,
                     PenToolButton,
                     TextToolButton
                 })
        {
            button.Background = button == selectedButton
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(MediaColor.FromRgb(57, 65, 80));
        }

        AnnotationCanvas.Cursor = tool == ToolMode.Pointer ? Cursors.Arrow : Cursors.Cross;
        StatusText.Text = tool switch
        {
            ToolMode.Rectangle => "拖动鼠标绘制矩形。",
            ToolMode.Pen => "按住鼠标自由绘制。",
            ToolMode.Text => "点击图片位置添加文字。",
            _ => "选择标注工具，或直接识别、翻译、复制图片。"
        };
    }

    private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string colorText)
        {
            _drawingBrush = (Brush)new BrushConverter().ConvertFromString(colorText)!;
        }
    }

    private void ThicknessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThicknessComboBox.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), out double thickness))
        {
            _drawingThickness = thickness;
        }
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(AnnotationCanvas);

        if (_tool == ToolMode.Text)
        {
            AddText(point);
            e.Handled = true;
            return;
        }

        if (_tool == ToolMode.Pointer)
        {
            return;
        }

        _start = point;
        _drawing = true;
        AnnotationCanvas.CaptureMouse();

        if (_tool == ToolMode.Rectangle)
        {
            Rectangle rectangle = new()
            {
                Stroke = _drawingBrush,
                StrokeThickness = _drawingThickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rectangle, point.X);
            Canvas.SetTop(rectangle, point.Y);
            _activeShape = rectangle;
        }
        else
        {
            Polyline line = new()
            {
                Stroke = _drawingBrush,
                StrokeThickness = _drawingThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            line.Points.Add(point);
            _activeShape = line;
        }

        AnnotationCanvas.Children.Add(_activeShape);
        _annotations.Add(_activeShape);
        e.Handled = true;
    }

    private void AnnotationCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_drawing || _activeShape is null)
        {
            return;
        }

        Point current = e.GetPosition(AnnotationCanvas);
        if (_activeShape is Rectangle rectangle)
        {
            double left = Math.Min(_start.X, current.X);
            double top = Math.Min(_start.Y, current.Y);
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            rectangle.Width = Math.Abs(current.X - _start.X);
            rectangle.Height = Math.Abs(current.Y - _start.Y);
        }
        else if (_activeShape is Polyline line)
        {
            line.Points.Add(current);
        }
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing)
        {
            return;
        }

        _drawing = false;
        _activeShape = null;
        AnnotationCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void AddText(Point point)
    {
        TextPromptDialog dialog = new()
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Border container = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(170, 0, 0, 0)),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = dialog.TextValue,
                Foreground = _drawingBrush,
                FontSize = 18 + _drawingThickness,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(180, AnnotationCanvas.Width - point.X - 10)
            }
        };
        Canvas.SetLeft(container, point.X);
        Canvas.SetTop(container, point.Y);
        AnnotationCanvas.Children.Add(container);
        _annotations.Add(container);
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0)
        {
            return;
        }

        UIElement last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        AnnotationCanvas.Children.Remove(last);
        StatusText.Text = "已撤销上一条标注。";
    }

    private async void OcrButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RecognizeAsync(force: true);
        }
        catch (Exception ex)
        {
            ShowWorkError(ex);
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            OcrRecognitionResult recognition = await RecognizeAsync(force: false);
            if (recognition.Lines.Count == 0)
            {
                throw new InvalidOperationException("截图中没有识别到文字。");
            }

            string targetLanguage = GetTargetLanguageName();
            SetBusy(true, $"正在翻译为{targetLanguage}并排版到图片…");
            IReadOnlyList<string> translatedLines =
                await _translationService.TranslateLinesAsync(
                    recognition.Lines.Select(line => line.Text).ToArray(),
                    _workCancellation.Token);
            TranslatedTextBox.Text = string.Join(
                Environment.NewLine,
                translatedLines);
            ApplyTranslationOverlays(
                recognition.Lines,
                translatedLines);
            ShowResults();
            StatusText.Text =
                $"已翻译为{targetLanguage}，译文已覆盖在图片对应位置。";
        }
        catch (Exception ex)
        {
            ShowWorkError(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<OcrRecognitionResult> RecognizeAsync(bool force)
    {
        if (_busy)
        {
            return _recognition ?? OcrRecognitionResult.Empty;
        }

        if (!force && _recognition is not null)
        {
            return _recognition;
        }

        SetBusy(true, "正在识别文字和位置…");
        try
        {
            OcrRecognitionResult recognition =
                await OcrService.RecognizeDetailedAsync(
                    _sourceBitmap,
                    _settings.OcrLanguage,
                    _workCancellation.Token);
            _recognition = recognition;
            OriginalTextBox.Text = recognition.Text;
            if (force)
            {
                TranslatedTextBox.Clear();
                ClearTranslationOverlays();
            }

            ShowResults();
            string recognizerDescription =
                OcrService.GetRecognizerDescription(
                    recognition.RecognizerLanguageTag);
            string recognitionMode =
                string.Equals(
                    _settings.OcrLanguage,
                    OcrService.AutoLanguageTag,
                    StringComparison.OrdinalIgnoreCase)
                    ? $"自动识别：{recognizerDescription}"
                    : recognizerDescription;
            StatusText.Text = recognition.Lines.Count > 0
                ? $"文字识别完成（{recognitionMode}），共 {recognition.Lines.Count} 行。"
                : "没有识别到文字。";
            return recognition;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyTranslationOverlays(
        IReadOnlyList<OcrTextLine> sourceLines,
        IReadOnlyList<string> translatedLines)
    {
        ClearTranslationOverlays();

        int count = Math.Min(sourceLines.Count, translatedLines.Count);
        for (int index = 0; index < count; index++)
        {
            string translation = translatedLines[index].Trim();
            if (translation.Length == 0)
            {
                continue;
            }

            OcrTextLine sourceLine = sourceLines[index];
            Border overlay = CreateTranslationOverlay(sourceLine, translation);
            TranslationCanvas.Children.Add(overlay);
        }

        ClearTranslationButton.IsEnabled = TranslationCanvas.Children.Count > 0;
    }

    private Border CreateTranslationOverlay(OcrTextLine sourceLine, string translation)
    {
        double canvasWidth = Math.Max(1, TranslationCanvas.Width);
        double canvasHeight = Math.Max(1, TranslationCanvas.Height);
        double fontSize = Math.Clamp(sourceLine.Height * 0.78, 12, 38);
        double estimatedTextWidth = translation.Sum(
            character => character > 255 ? fontSize : fontSize * 0.58);
        double minimumWidth = Math.Min(
            canvasWidth,
            Math.Max(72, sourceLine.Width + 10));
        double maximumWidth = Math.Max(
            minimumWidth,
            Math.Min(
                canvasWidth,
                Math.Max(260, sourceLine.Width * 2.8)));
        double width = Math.Clamp(
            Math.Max(sourceLine.Width + 10, estimatedTextWidth + 18),
            minimumWidth,
            maximumWidth);
        double height = Math.Min(
            canvasHeight,
            Math.Max(28, sourceLine.Height + 10));
        double left = Math.Clamp(
            sourceLine.X - Math.Max(0, width - sourceLine.Width) / 2,
            0,
            Math.Max(0, canvasWidth - width));
        double top = Math.Clamp(
            sourceLine.Y - 5,
            0,
            Math.Max(0, canvasHeight - height));

        TextBlock text = new()
        {
            Text = translation,
            Foreground = Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Viewbox textFitter = new()
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = text
        };
        Border overlay = new()
        {
            Width = width,
            Height = height,
            Padding = new Thickness(5, 2, 5, 2),
            Background = new SolidColorBrush(MediaColor.FromArgb(224, 15, 18, 24)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(150, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = textFitter
        };

        Canvas.SetLeft(overlay, left);
        Canvas.SetTop(overlay, top);
        return overlay;
    }

    private void ClearTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        ClearTranslationOverlays();
        TranslatedTextBox.Clear();
        StatusText.Text = "已从图片中清除译文。";
    }

    private void ClearTranslationOverlays()
    {
        TranslationCanvas.Children.Clear();
        ClearTranslationButton.IsEnabled = false;
    }

    private string GetTargetLanguageName() =>
        AppSettings.TargetLanguages
            .FirstOrDefault(
                language => string.Equals(
                    language.Code,
                    _settings.TargetLanguage,
                    StringComparison.OrdinalIgnoreCase))
            ?.Name
        ?? _settings.TargetLanguage;

    private void ShowResults()
    {
        ResultsColumn.Width = new GridLength(360);
        ResultsPanel.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        OcrButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy;
        ClearTranslationButton.IsEnabled =
            !busy && TranslationCanvas.Children.Count > 0;
        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusText.Text = status;
        }
    }

    private void ShowWorkError(Exception exception)
    {
        SetBusy(false);
        StatusText.Text = exception.Message;
        MessageBox.Show(
            this,
            exception.Message,
            "操作失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void CopyOriginalTextButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(OriginalTextBox.Text, "原文已复制。");
    }

    private void CopyTranslatedTextButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(TranslatedTextBox.Text, "译文已复制。");
    }

    private void CopyText(string text, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText.Text = "没有可复制的文字。";
            return;
        }

        Clipboard.SetText(text);
        StatusText.Text = successMessage;
    }

    private void CopyImageButton_Click(object sender, RoutedEventArgs e)
    {
        BitmapSource result = RenderComposite();
        Clipboard.SetImage(result);
        StatusText.Text = "图片已复制到剪贴板。";
        Close();
    }

    private void SaveImageButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png",
            FileName = $"LingxiCapture-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            AddExtension = true,
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        BitmapSource result = RenderComposite();
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(result));
        using FileStream stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        StatusText.Text = $"已保存：{dialog.FileName}";
    }

    private BitmapSource RenderComposite()
    {
        DocumentGrid.Measure(new Size(_source.PixelWidth, _source.PixelHeight));
        DocumentGrid.Arrange(new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        DocumentGrid.UpdateLayout();

        RenderTargetBitmap target = new(
            _source.PixelWidth,
            _source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        target.Render(DocumentGrid);
        target.Freeze();
        return target;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EditorWindow_Closed(object? sender, EventArgs e)
    {
        _workCancellation.Cancel();
        _workCancellation.Dispose();
        _sourceBitmap.Dispose();
    }
}
