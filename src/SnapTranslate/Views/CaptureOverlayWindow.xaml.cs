using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using SnapTranslate.Models;
using SnapTranslate.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Clipboard = System.Windows.Clipboard;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Size = System.Windows.Size;

namespace SnapTranslate.Views;

public partial class CaptureOverlayWindow : Window
{
    private enum ToolMode
    {
        Selection,
        Rectangle,
        Ellipse,
        Arrow,
        Pen,
        Mosaic,
        Text
    }

    private readonly CapturedScreen _capture;
    private readonly AppSettings _settings;
    private readonly TranslationService _translationService;
    private readonly CancellationTokenSource _workCancellation = new();
    private readonly List<UIElement> _annotations = [];
    private readonly DispatcherTimer _statusTimer;

    private Bitmap? _selectionBitmap;
    private OcrRecognitionResult? _recognition;
    private ToolMode _tool = ToolMode.Selection;
    private Brush _drawingBrush = new SolidColorBrush(MediaColor.FromRgb(255, 77, 79));
    private double _drawingThickness = 4;
    private Point _start;
    private Rect _selection;
    private Int32Rect _selectionPixelRect;
    private Shape? _activeShape;
    private Canvas? _activeArrow;
    private Canvas? _activeMosaicStroke;
    private HashSet<(int X, int Y)>? _activeMosaicCells;
    private Point? _lastMosaicPoint;
    private bool _selecting;
    private bool _drawing;
    private bool _selectionReady;
    private bool _accepted;
    private bool _busy;

    public CaptureOverlayWindow(CapturedScreen capture, AppSettings settings)
    {
        InitializeComponent();

        _capture = capture;
        _settings = settings;
        _translationService = new TranslationService(settings);
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.8)
        };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusToast.Visibility = Visibility.Collapsed;
        };

        Left = capture.Bounds.Left;
        Top = capture.Bounds.Top;
        Width = capture.Bounds.Width;
        Height = capture.Bounds.Height;
        ScreenshotImage.Source = capture.Preview;

        Loaded += (_, _) =>
        {
            Focus();
            UpdateMask(Rect.Empty);
        };
        Closed += CaptureOverlayWindow_Closed;
    }

    public event Action<Bitmap>? AdvancedEditRequested;
    public event Action? CaptureCompleted;
    public event Action? CaptureCancelled;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFloatingUiElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Point point = ClampPoint(e.GetPosition(RootGrid));

        if (_selectionReady &&
            e.ClickCount >= 2 &&
            _tool != ToolMode.Text &&
            _selection.Contains(point))
        {
            CompleteAndCopy();
            e.Handled = true;
            return;
        }

        if (_selectionReady &&
            _tool == ToolMode.Selection &&
            _selection.Contains(point))
        {
            e.Handled = true;
            return;
        }

        if (_selectionReady && _tool != ToolMode.Selection)
        {
            if (!_selection.Contains(point))
            {
                return;
            }

            Point localPoint = ToSelectionPoint(point);
            if (_tool == ToolMode.Text)
            {
                AddText(localPoint);
                e.Handled = true;
                return;
            }

            BeginAnnotation(localPoint);
            e.Handled = true;
            return;
        }

        BeginSelection(point);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_selecting)
        {
            Point current = ClampPoint(e.GetPosition(RootGrid));
            _selection = Normalize(_start, current);
            UpdateMask(_selection);
            return;
        }

        if (!_drawing)
        {
            return;
        }

        Point localPoint = ClampSelectionPoint(ToSelectionPoint(e.GetPosition(RootGrid)));
        if (_activeArrow is not null)
        {
            UpdateArrow(_activeArrow, _start, localPoint);
        }
        else if (_activeMosaicStroke is not null)
        {
            AddMosaicStrokeTo(localPoint);
        }
        else if (_activeShape is Rectangle or Ellipse)
        {
            Shape shape = _activeShape;
            double left = Math.Min(_start.X, localPoint.X);
            double top = Math.Min(_start.Y, localPoint.Y);
            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);
            shape.Width = Math.Abs(localPoint.X - _start.X);
            shape.Height = Math.Abs(localPoint.Y - _start.Y);
        }
        else if (_activeShape is Polyline line)
        {
            line.Points.Add(localPoint);
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();

            if (_selection.Width < 8 || _selection.Height < 8)
            {
                ResetSelection();
                return;
            }

            PrepareSelection();
            e.Handled = true;
            return;
        }

        if (_drawing)
        {
            _drawing = false;
            _activeShape = null;
            _activeArrow = null;
            _activeMosaicStroke = null;
            _activeMosaicCells = null;
            _lastMosaicPoint = null;
            ReleaseMouseCapture();
            ShowStatus("标注已添加", PackIconMaterialKind.CheckCircleOutline);
            e.Handled = true;
        }
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Return or Key.Enter && _selectionReady && !_busy)
        {
            CompleteAndCopy();
            e.Handled = true;
        }
    }

    private void BeginSelection(Point point)
    {
        ClearSelectionContent();
        _start = point;
        _selection = new Rect(point, point);
        _selecting = true;
        _tool = ToolMode.Selection;
        Cursor = Cursors.Cross;
        CaptureMouse();
        InitialHint.Visibility = Visibility.Collapsed;
        QuickToolbar.Visibility = Visibility.Collapsed;
        ResultsPopover.Visibility = Visibility.Collapsed;
        ToolOptionsPanel.Visibility = Visibility.Collapsed;
        UpdateMask(_selection);
    }

    private void PrepareSelection()
    {
        double scaleX = _capture.Bitmap.Width / Math.Max(1, RootGrid.ActualWidth);
        double scaleY = _capture.Bitmap.Height / Math.Max(1, RootGrid.ActualHeight);
        _selectionPixelRect = new Int32Rect(
            (int)Math.Round(_selection.X * scaleX),
            (int)Math.Round(_selection.Y * scaleY),
            Math.Max(1, (int)Math.Round(_selection.Width * scaleX)),
            Math.Max(1, (int)Math.Round(_selection.Height * scaleY)));

        _selectionBitmap?.Dispose();
        _selectionBitmap = ScreenCaptureService.Crop(_capture.Bitmap, _selectionPixelRect);
        SelectionImage.Source = ScreenCaptureService.ToBitmapSource(_selectionBitmap);

        SelectionContent.Width = _selection.Width;
        SelectionContent.Height = _selection.Height;
        TranslationCanvas.Width = _selection.Width;
        TranslationCanvas.Height = _selection.Height;
        AnnotationCanvas.Width = _selection.Width;
        AnnotationCanvas.Height = _selection.Height;
        Canvas.SetLeft(SelectionContent, _selection.Left);
        Canvas.SetTop(SelectionContent, _selection.Top);
        SelectionContent.Visibility = Visibility.Visible;

        _selectionReady = true;
        QuickToolbar.Visibility = Visibility.Visible;
        HandleCanvas.Visibility = Visibility.Visible;
        Cursor = Cursors.Arrow;
        UpdateMask(_selection);
        SetTool(ToolMode.Selection, SelectionToolButton);
        Dispatcher.BeginInvoke(PositionFloatingUi, DispatcherPriority.Loaded);
    }

    private void BeginAnnotation(Point point)
    {
        _start = point;
        _drawing = true;
        CaptureMouse();

        if (_tool == ToolMode.Rectangle || _tool == ToolMode.Ellipse)
        {
            Shape shape = _tool == ToolMode.Rectangle
                ? new Rectangle()
                : new Ellipse();
            shape.Stroke = _drawingBrush;
            shape.StrokeThickness = _drawingThickness;
            shape.Fill = Brushes.Transparent;
            Canvas.SetLeft(shape, point.X);
            Canvas.SetTop(shape, point.Y);
            _activeShape = shape;
            AddActiveAnnotation(shape);
        }
        else if (_tool == ToolMode.Arrow)
        {
            Canvas arrow = new()
            {
                Width = AnnotationCanvas.Width,
                Height = AnnotationCanvas.Height,
                IsHitTestVisible = false
            };
            CreateArrowLines(arrow);
            UpdateArrow(arrow, point, point);
            _activeArrow = arrow;
            AddActiveAnnotation(arrow);
        }
        else if (_tool == ToolMode.Mosaic)
        {
            Canvas stroke = new()
            {
                Width = AnnotationCanvas.Width,
                Height = AnnotationCanvas.Height,
                IsHitTestVisible = false
            };
            _activeMosaicStroke = stroke;
            _activeMosaicCells = [];
            _lastMosaicPoint = point;
            AddActiveAnnotation(stroke);
            AddMosaicStamp(point);
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
            AddActiveAnnotation(line);
        }
    }

    private void AddActiveAnnotation(UIElement annotation)
    {
        AnnotationCanvas.Children.Add(annotation);
        _annotations.Add(annotation);
    }

    private void CreateArrowLines(Canvas arrow)
    {
        for (int index = 0; index < 3; index++)
        {
            arrow.Children.Add(new Line
            {
                Stroke = _drawingBrush,
                StrokeThickness = _drawingThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }
    }

    private static void UpdateArrow(Canvas arrow, Point start, Point end)
    {
        if (arrow.Children.Count != 3 ||
            arrow.Children[0] is not Line shaft ||
            arrow.Children[1] is not Line headOne ||
            arrow.Children[2] is not Line headTwo)
        {
            return;
        }

        shaft.X1 = start.X;
        shaft.Y1 = start.Y;
        shaft.X2 = end.X;
        shaft.Y2 = end.Y;

        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        double angle = Math.Atan2(deltaY, deltaX);
        double headLength = Math.Clamp(distance * 0.25, 12, 32);
        const double headAngle = Math.PI / 6;

        SetArrowHeadLine(headOne, end, angle + Math.PI - headAngle, headLength);
        SetArrowHeadLine(headTwo, end, angle + Math.PI + headAngle, headLength);
    }

    private static void SetArrowHeadLine(
        Line line,
        Point end,
        double angle,
        double length)
    {
        line.X1 = end.X;
        line.Y1 = end.Y;
        line.X2 = end.X + Math.Cos(angle) * length;
        line.Y2 = end.Y + Math.Sin(angle) * length;
    }

    private void AddMosaicStamp(Point point)
    {
        if (_activeMosaicStroke is null ||
            _activeMosaicCells is null ||
            _selectionBitmap is null)
        {
            return;
        }

        double cellSize = Math.Clamp(8 + _drawingThickness, 10, 18);
        double brushRadius = Math.Clamp(18 + _drawingThickness * 2, 22, 40);
        int minimumX = Math.Max(0, (int)Math.Floor((point.X - brushRadius) / cellSize));
        int maximumX = Math.Min(
            (int)Math.Ceiling(AnnotationCanvas.Width / cellSize) - 1,
            (int)Math.Floor((point.X + brushRadius) / cellSize));
        int minimumY = Math.Max(0, (int)Math.Floor((point.Y - brushRadius) / cellSize));
        int maximumY = Math.Min(
            (int)Math.Ceiling(AnnotationCanvas.Height / cellSize) - 1,
            (int)Math.Floor((point.Y + brushRadius) / cellSize));

        double pixelScaleX = _selectionBitmap.Width / Math.Max(1, AnnotationCanvas.Width);
        double pixelScaleY = _selectionBitmap.Height / Math.Max(1, AnnotationCanvas.Height);
        for (int cellY = minimumY; cellY <= maximumY; cellY++)
        {
            for (int cellX = minimumX; cellX <= maximumX; cellX++)
            {
                double cellCenterX = (cellX + 0.5) * cellSize;
                double cellCenterY = (cellY + 0.5) * cellSize;
                double deltaX = cellCenterX - point.X;
                double deltaY = cellCenterY - point.Y;
                if (deltaX * deltaX + deltaY * deltaY > brushRadius * brushRadius ||
                    !_activeMosaicCells.Add((cellX, cellY)))
                {
                    continue;
                }

                int pixelX = Math.Clamp(
                    (int)Math.Round(cellCenterX * pixelScaleX),
                    0,
                    _selectionBitmap.Width - 1);
                int pixelY = Math.Clamp(
                    (int)Math.Round(cellCenterY * pixelScaleY),
                    0,
                    _selectionBitmap.Height - 1);
                System.Drawing.Color sampled = _selectionBitmap.GetPixel(pixelX, pixelY);
                Rectangle tile = new()
                {
                    Width = Math.Min(
                        cellSize + 0.75,
                        AnnotationCanvas.Width - cellX * cellSize),
                    Height = Math.Min(
                        cellSize + 0.75,
                        AnnotationCanvas.Height - cellY * cellSize),
                    Fill = new SolidColorBrush(
                        MediaColor.FromRgb(sampled.R, sampled.G, sampled.B)),
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(tile, cellX * cellSize);
                Canvas.SetTop(tile, cellY * cellSize);
                _activeMosaicStroke.Children.Add(tile);
            }
        }
    }

    private void AddMosaicStrokeTo(Point point)
    {
        Point previous = _lastMosaicPoint ?? point;
        double deltaX = point.X - previous.X;
        double deltaY = point.Y - previous.Y;
        double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        double step = Math.Max(5, 7 + _drawingThickness * 0.5);
        int stampCount = Math.Max(1, (int)Math.Ceiling(distance / step));
        for (int index = 1; index <= stampCount; index++)
        {
            double progress = index / (double)stampCount;
            AddMosaicStamp(new Point(
                previous.X + deltaX * progress,
                previous.Y + deltaY * progress));
        }

        _lastMosaicPoint = point;
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
            Background = new SolidColorBrush(MediaColor.FromArgb(178, 0, 0, 0)),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = dialog.TextValue,
                Foreground = _drawingBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18 + _drawingThickness,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(120, AnnotationCanvas.Width - point.X - 8)
            }
        };
        Canvas.SetLeft(container, point.X);
        Canvas.SetTop(container, point.Y);
        AnnotationCanvas.Children.Add(container);
        _annotations.Add(container);
        ShowStatus("文字已添加", PackIconMaterialKind.CheckCircleOutline);
    }

    private void SelectionToolButton_Click(object sender, RoutedEventArgs e)
    {
        SetTool(ToolMode.Selection, SelectionToolButton);
        ShowStatus("拖动鼠标可重新选择区域", PackIconMaterialKind.SelectionDrag);
    }

    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Rectangle, RectangleToolButton);

    private void EllipseToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Ellipse, EllipseToolButton);

    private void ArrowToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Arrow, ArrowToolButton);

    private void PenToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Pen, PenToolButton);

    private void MosaicToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Mosaic, MosaicToolButton);

    private void TextToolButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(ToolMode.Text, TextToolButton);

    private void SetTool(ToolMode tool, Button selectedButton)
    {
        _tool = tool;
        foreach (Button button in new[]
                 {
                     SelectionToolButton,
                     RectangleToolButton,
                     EllipseToolButton,
                     ArrowToolButton,
                     PenToolButton,
                     MosaicToolButton,
                     TextToolButton
                 })
        {
            button.Background = button == selectedButton
                ? new SolidColorBrush(MediaColor.FromRgb(52, 87, 158))
                : Brushes.Transparent;
        }

        ToolOptionsPanel.Visibility = Visibility.Collapsed;
        Cursor = tool == ToolMode.Selection ? Cursors.Arrow : Cursors.Cross;
    }

    private void ColorToolButton_Click(object sender, RoutedEventArgs e)
    {
        ColorOptions.Visibility = Visibility.Visible;
        ThicknessOptions.Visibility = Visibility.Collapsed;
        ToggleToolOptions();
    }

    private void ThicknessToolButton_Click(object sender, RoutedEventArgs e)
    {
        ColorOptions.Visibility = Visibility.Collapsed;
        ThicknessOptions.Visibility = Visibility.Visible;
        ToggleToolOptions();
    }

    private void ToggleToolOptions()
    {
        ToolOptionsPanel.Visibility =
            ToolOptionsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        PositionFloatingUi();
    }

    private void RedColorButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingColor(MediaColor.FromRgb(255, 77, 79), "红色");

    private void YellowColorButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingColor(MediaColor.FromRgb(255, 212, 59), "黄色");

    private void GreenColorButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingColor(MediaColor.FromRgb(46, 204, 113), "绿色");

    private void BlueColorButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingColor(MediaColor.FromRgb(79, 124, 255), "蓝色");

    private void WhiteColorButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingColor(Colors.White, "白色");

    private void SetDrawingColor(MediaColor color, string name)
    {
        _drawingBrush = new SolidColorBrush(color);
        ToolOptionsPanel.Visibility = Visibility.Collapsed;
        ShowStatus($"标注颜色：{name}", PackIconMaterialKind.PaletteOutline);
    }

    private void ThinButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingThickness(2);

    private void NormalButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingThickness(4);

    private void BoldButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingThickness(6);

    private void HeavyButton_Click(object sender, RoutedEventArgs e) =>
        SetDrawingThickness(10);

    private void SetDrawingThickness(double thickness)
    {
        _drawingThickness = thickness;
        ToolOptionsPanel.Visibility = Visibility.Collapsed;
        ShowStatus($"线条粗细：{thickness:0} px", PackIconMaterialKind.FormatLineWeight);
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0)
        {
            ShowStatus("没有可撤销的标注", PackIconMaterialKind.InformationOutline);
            return;
        }

        UIElement last = _annotations[^1];
        _annotations.RemoveAt(_annotations.Count - 1);
        AnnotationCanvas.Children.Remove(last);
        ShowStatus("已撤销上一条标注", PackIconMaterialKind.Undo);
    }

    private async void OcrButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RecognizeAsync(force: true);
        }
        catch (Exception exception)
        {
            ShowWorkError(exception);
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
            SetBusy(true, $"正在翻译为{targetLanguage}…");
            IReadOnlyList<string> translatedLines =
                await _translationService.TranslateLinesAsync(
                    recognition.Lines.Select(line => line.Text).ToArray(),
                    _workCancellation.Token);
            TranslatedTextBlock.Text = string.Join(Environment.NewLine, translatedLines);
            ApplyTranslationOverlays(recognition.Lines, translatedLines);
            ResultsPopover.Visibility = Visibility.Visible;
            PositionFloatingUi();
            ShowStatus($"已翻译为{targetLanguage}", PackIconMaterialKind.Translate);
        }
        catch (Exception exception)
        {
            ShowWorkError(exception);
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

        if (_selectionBitmap is null)
        {
            throw new InvalidOperationException("请先选择截图区域。");
        }

        SetBusy(true, "正在识别文字…");
        try
        {
            OcrRecognitionResult recognition =
                await OcrService.RecognizeDetailedAsync(
                    _selectionBitmap,
                    _settings.OcrLanguage,
                    _workCancellation.Token);
            _recognition = recognition;
            OriginalTextBlock.Text = recognition.Text;
            if (force)
            {
                TranslatedTextBlock.Text = string.Empty;
                TranslationCanvas.Children.Clear();
            }

            ResultsPopover.Visibility = Visibility.Visible;
            PositionFloatingUi();
            string description =
                OcrService.GetRecognizerDescription(recognition.RecognizerLanguageTag);
            ShowStatus(
                recognition.Lines.Count > 0
                    ? $"识别完成（{description}），共 {recognition.Lines.Count} 行"
                    : "没有识别到文字",
                recognition.Lines.Count > 0
                    ? PackIconMaterialKind.TextRecognition
                    : PackIconMaterialKind.InformationOutline);
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
        TranslationCanvas.Children.Clear();
        if (_selectionBitmap is null)
        {
            return;
        }

        double scaleX = _selection.Width / Math.Max(1, _selectionBitmap.Width);
        double scaleY = _selection.Height / Math.Max(1, _selectionBitmap.Height);
        int count = Math.Min(sourceLines.Count, translatedLines.Count);
        for (int index = 0; index < count; index++)
        {
            string translation = translatedLines[index].Trim();
            if (translation.Length == 0)
            {
                continue;
            }

            OcrTextLine sourceLine = sourceLines[index];
            Border overlay = CreateTranslationOverlay(
                sourceLine.X * scaleX,
                sourceLine.Y * scaleY,
                sourceLine.Width * scaleX,
                sourceLine.Height * scaleY,
                translation);
            TranslationCanvas.Children.Add(overlay);
        }
    }

    private Border CreateTranslationOverlay(
        double sourceX,
        double sourceY,
        double sourceWidth,
        double sourceHeight,
        string translation)
    {
        double canvasWidth = Math.Max(1, TranslationCanvas.Width);
        double canvasHeight = Math.Max(1, TranslationCanvas.Height);
        double fontSize = Math.Clamp(sourceHeight * 0.78, 12, 36);
        double estimatedTextWidth = translation.Sum(
            character => character > 255 ? fontSize : fontSize * 0.58);
        double minimumWidth = Math.Min(canvasWidth, Math.Max(72, sourceWidth + 10));
        double maximumWidth = Math.Max(
            minimumWidth,
            Math.Min(canvasWidth, Math.Max(260, sourceWidth * 2.8)));
        double width = Math.Clamp(
            Math.Max(sourceWidth + 10, estimatedTextWidth + 18),
            minimumWidth,
            maximumWidth);
        double height = Math.Min(canvasHeight, Math.Max(28, sourceHeight + 10));
        double left = Math.Clamp(
            sourceX - Math.Max(0, width - sourceWidth) / 2,
            0,
            Math.Max(0, canvasWidth - width));
        double top = Math.Clamp(sourceY - 5, 0, Math.Max(0, canvasHeight - height));

        TextBlock text = new()
        {
            Text = translation,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Viewbox fitter = new()
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
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(155, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = fitter
        };
        Canvas.SetLeft(overlay, left);
        Canvas.SetTop(overlay, top);
        return overlay;
    }

    private void CopyOriginalTextButton_Click(object sender, RoutedEventArgs e) =>
        CopyText(OriginalTextBlock.Text, "原文已复制");

    private void CopyTranslatedTextButton_Click(object sender, RoutedEventArgs e) =>
        CopyText(TranslatedTextBlock.Text, "译文已复制");

    private void CopyText(string text, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowStatus("没有可复制的文字", PackIconMaterialKind.InformationOutline);
            return;
        }

        Clipboard.SetText(text);
        ShowStatus(successMessage, PackIconMaterialKind.ContentCopy);
    }

    private void SaveImageButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png",
            FileName = $"SnapTranslate-{DateTime.Now:yyyyMMdd-HHmmss}.png",
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
        ShowStatus("PNG 已保存", PackIconMaterialKind.ContentSaveOutline);
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e) =>
        CompleteAndCopy();

    private void CompleteAndCopy()
    {
        if (!_selectionReady || _busy)
        {
            return;
        }

        try
        {
            CopyCompositeToClipboard();
            _accepted = true;
            Close();
            CaptureCompleted?.Invoke();
        }
        catch (Exception exception)
        {
            ShowWorkError(exception);
        }
    }

    private void CopyCompositeToClipboard()
    {
        BitmapSource result = RenderComposite();
        ExternalException? clipboardException = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetImage(result);
                Clipboard.Flush();
                return;
            }
            catch (ExternalException exception)
            {
                clipboardException = exception;
                if (attempt < 4)
                {
                    Thread.Sleep(35 * (attempt + 1));
                }
            }
        }

        throw new InvalidOperationException(
            "剪贴板正被其他程序占用，请稍后再试。",
            clipboardException);
    }

    private void AdvancedEditorButton_Click(object sender, RoutedEventArgs e)
    {
        Bitmap bitmap = ScreenCaptureService.ToBitmap(RenderComposite());
        _accepted = true;
        Close();
        AdvancedEditRequested?.Invoke(bitmap);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private BitmapSource RenderComposite()
    {
        if (!_selectionReady || _selectionBitmap is null)
        {
            throw new InvalidOperationException("请先选择截图区域。");
        }

        SelectionContent.Measure(new Size(_selection.Width, _selection.Height));
        SelectionContent.Arrange(new Rect(0, 0, _selection.Width, _selection.Height));
        SelectionContent.UpdateLayout();

        double dpiX = 96 * _selectionBitmap.Width / Math.Max(1, _selection.Width);
        double dpiY = 96 * _selectionBitmap.Height / Math.Max(1, _selection.Height);
        RenderTargetBitmap target = new(
            _selectionBitmap.Width,
            _selectionBitmap.Height,
            dpiX,
            dpiY,
            PixelFormats.Pbgra32);
        target.Render(SelectionContent);
        target.Freeze();
        return target;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        OcrButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy;
        CompleteButton.IsEnabled = !busy;
        AdvancedEditorButton.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowStatus(message, PackIconMaterialKind.ProgressClock);
        }
    }

    private void ShowWorkError(Exception exception)
    {
        SetBusy(false);
        ShowStatus(exception.Message, PackIconMaterialKind.AlertCircleOutline);
        MessageBox.Show(
            this,
            exception.Message,
            "操作失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

    private void ShowStatus(string text, PackIconMaterialKind icon)
    {
        StatusText.Text = text;
        StatusIcon.Kind = icon;
        StatusIcon.Foreground =
            icon == PackIconMaterialKind.AlertCircleOutline
                ? Brushes.Orange
                : new SolidColorBrush(MediaColor.FromRgb(114, 213, 132));
        StatusToast.Visibility = Visibility.Visible;
        PositionFloatingUi();
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void PositionFloatingUi()
    {
        if (!_selectionReady)
        {
            return;
        }

        double viewportWidth = Math.Max(1, RootGrid.ActualWidth);
        double viewportHeight = Math.Max(1, RootGrid.ActualHeight);
        QuickToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double toolbarWidth = Math.Min(QuickToolbar.DesiredSize.Width, viewportWidth - 24);
        double toolbarHeight = QuickToolbar.DesiredSize.Height;
        double toolbarLeft = Math.Clamp(
            _selection.Left + (_selection.Width - toolbarWidth) / 2,
            12,
            Math.Max(12, viewportWidth - toolbarWidth - 12));
        double toolbarTop = _selection.Bottom + 12;
        if (toolbarTop + toolbarHeight > viewportHeight - 12)
        {
            toolbarTop = _selection.Top - toolbarHeight - 12;
        }

        toolbarTop = Math.Clamp(
            toolbarTop,
            12,
            Math.Max(12, viewportHeight - toolbarHeight - 12));
        QuickToolbar.MaxWidth = viewportWidth - 24;
        Canvas.SetLeft(QuickToolbar, toolbarLeft);
        Canvas.SetTop(QuickToolbar, toolbarTop);

        ToolOptionsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double optionsLeft = Math.Clamp(
            toolbarLeft + 250,
            12,
            Math.Max(12, viewportWidth - ToolOptionsPanel.DesiredSize.Width - 12));
        double optionsTop = toolbarTop - ToolOptionsPanel.DesiredSize.Height - 7;
        Canvas.SetLeft(ToolOptionsPanel, optionsLeft);
        Canvas.SetTop(ToolOptionsPanel, Math.Max(12, optionsTop));

        ResultsPopover.Measure(new Size(390, double.PositiveInfinity));
        double resultLeft = Math.Clamp(
            toolbarLeft + toolbarWidth * 0.52,
            12,
            Math.Max(12, viewportWidth - ResultsPopover.DesiredSize.Width - 12));
        double resultTop = toolbarTop + toolbarHeight + 8;
        if (resultTop + ResultsPopover.DesiredSize.Height > viewportHeight - 12)
        {
            resultTop = toolbarTop - ResultsPopover.DesiredSize.Height - 8;
        }

        Canvas.SetLeft(ResultsPopover, resultLeft);
        Canvas.SetTop(ResultsPopover, Math.Max(12, resultTop));

        StatusToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double statusLeft = Math.Clamp(
            toolbarLeft,
            12,
            Math.Max(12, viewportWidth - StatusToast.DesiredSize.Width - 12));
        double statusTop = toolbarTop - StatusToast.DesiredSize.Height - 8;
        Canvas.SetLeft(StatusToast, statusLeft);
        Canvas.SetTop(StatusToast, Math.Max(12, statusTop));
    }

    private void UpdateMask(Rect selection)
    {
        double width = Math.Max(0, RootGrid.ActualWidth);
        double height = Math.Max(0, RootGrid.ActualHeight);

        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            SetCanvasRect(TopShade, 0, 0, width, height);
            SetCanvasRect(BottomShade, 0, 0, 0, 0);
            SetCanvasRect(LeftShade, 0, 0, 0, 0);
            SetCanvasRect(RightShade, 0, 0, 0, 0);
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            HandleCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        SetCanvasRect(TopShade, 0, 0, width, selection.Top);
        SetCanvasRect(BottomShade, 0, selection.Bottom, width, height - selection.Bottom);
        SetCanvasRect(LeftShade, 0, selection.Top, selection.Left, selection.Height);
        SetCanvasRect(
            RightShade,
            selection.Right,
            selection.Top,
            width - selection.Right,
            selection.Height);
        SetCanvasRect(
            SelectionBorder,
            selection.Left,
            selection.Top,
            selection.Width,
            selection.Height);
        SelectionBorder.Visibility = Visibility.Visible;

        SizeText.Text =
            $"{Math.Round(selection.Width)} × {Math.Round(selection.Height)}";
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeTop = selection.Top > 34
            ? selection.Top - SizeBadge.DesiredSize.Height - 5
            : selection.Bottom + 5;
        Canvas.SetLeft(SizeBadge, selection.Left);
        Canvas.SetTop(SizeBadge, badgeTop);
        SizeBadge.Visibility = Visibility.Visible;
        PositionSelectionHandles(selection);
    }

    private void PositionSelectionHandles(Rect selection)
    {
        if (!_selectionReady)
        {
            HandleCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        const double offset = 4.5;
        PositionHandle(TopLeftHandle, selection.Left - offset, selection.Top - offset);
        PositionHandle(
            TopCenterHandle,
            selection.Left + selection.Width / 2 - offset,
            selection.Top - offset);
        PositionHandle(TopRightHandle, selection.Right - offset, selection.Top - offset);
        PositionHandle(
            MiddleLeftHandle,
            selection.Left - offset,
            selection.Top + selection.Height / 2 - offset);
        PositionHandle(
            MiddleRightHandle,
            selection.Right - offset,
            selection.Top + selection.Height / 2 - offset);
        PositionHandle(BottomLeftHandle, selection.Left - offset, selection.Bottom - offset);
        PositionHandle(
            BottomCenterHandle,
            selection.Left + selection.Width / 2 - offset,
            selection.Bottom - offset);
        PositionHandle(BottomRightHandle, selection.Right - offset, selection.Bottom - offset);
        HandleCanvas.Visibility = Visibility.Visible;
    }

    private static void PositionHandle(FrameworkElement handle, double left, double top)
    {
        Canvas.SetLeft(handle, left);
        Canvas.SetTop(handle, top);
    }

    private void ResetSelection()
    {
        _selection = Rect.Empty;
        ClearSelectionContent();
        InitialHint.Visibility = Visibility.Visible;
        Cursor = Cursors.Cross;
        UpdateMask(Rect.Empty);
    }

    private void ClearSelectionContent()
    {
        _selectionReady = false;
        _recognition = null;
        _selectionBitmap?.Dispose();
        _selectionBitmap = null;
        _annotations.Clear();
        AnnotationCanvas.Children.Clear();
        TranslationCanvas.Children.Clear();
        OriginalTextBlock.Text = string.Empty;
        TranslatedTextBlock.Text = string.Empty;
        SelectionContent.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        QuickToolbar.Visibility = Visibility.Collapsed;
        ResultsPopover.Visibility = Visibility.Collapsed;
        ToolOptionsPanel.Visibility = Visibility.Collapsed;
        StatusToast.Visibility = Visibility.Collapsed;
    }

    private Point ToSelectionPoint(Point rootPoint) =>
        new(rootPoint.X - _selection.Left, rootPoint.Y - _selection.Top);

    private Point ClampSelectionPoint(Point point) =>
        new(
            Math.Clamp(point.X, 0, Math.Max(0, _selection.Width)),
            Math.Clamp(point.Y, 0, Math.Max(0, _selection.Height)));

    private Point ClampPoint(Point point) =>
        new(
            Math.Clamp(point.X, 0, Math.Max(0, RootGrid.ActualWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, RootGrid.ActualHeight)));

    private static Rect Normalize(Point first, Point second) =>
        new(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));

    private static void SetCanvasRect(
        FrameworkElement element,
        double left,
        double top,
        double width,
        double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private bool IsFloatingUiElement(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase or ScrollBar)
            {
                return true;
            }

            if (ReferenceEquals(current, QuickToolbar) ||
                ReferenceEquals(current, ResultsPopover) ||
                ReferenceEquals(current, ToolOptionsPanel))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void CaptureOverlayWindow_Closed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _workCancellation.Cancel();
        _workCancellation.Dispose();
        _selectionBitmap?.Dispose();
        _capture.Dispose();
        if (!_accepted)
        {
            CaptureCancelled?.Invoke();
        }
    }
}
