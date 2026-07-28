using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapTranslate.Services;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace SnapTranslate.Views;

public partial class CaptureOverlayWindow : Window
{
    private readonly CapturedScreen _capture;
    private Point _start;
    private Rect _selection;
    private bool _dragging;
    private bool _accepted;

    public CaptureOverlayWindow(CapturedScreen capture)
    {
        InitializeComponent();
        _capture = capture;

        Left = capture.Bounds.Left;
        Top = capture.Bounds.Top;
        Width = capture.Bounds.Width;
        Height = capture.Bounds.Height;
        ScreenshotImage.Source = capture.Preview;

        Loaded += (_, _) =>
        {
            Focus();
            UpdateMask(new Rect());
        };
        Closed += CaptureOverlayWindow_Closed;
    }

    public event Action<Bitmap>? CaptureAccepted;
    public event Action? CaptureCancelled;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = ClampPoint(e.GetPosition(RootGrid));
        _selection = new Rect(_start, _start);
        _dragging = true;
        CaptureMouse();
        UpdateMask(_selection);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point current = ClampPoint(e.GetPosition(RootGrid));
        _selection = Normalize(_start, current);
        UpdateMask(_selection);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        if (_selection.Width < 8 || _selection.Height < 8)
        {
            _selection = Rect.Empty;
            UpdateMask(_selection);
            return;
        }

        double scaleX = _capture.Bitmap.Width / Math.Max(1, RootGrid.ActualWidth);
        double scaleY = _capture.Bitmap.Height / Math.Max(1, RootGrid.ActualHeight);
        Int32Rect pixelRect = new(
            (int)Math.Round(_selection.X * scaleX),
            (int)Math.Round(_selection.Y * scaleY),
            Math.Max(1, (int)Math.Round(_selection.Width * scaleX)),
            Math.Max(1, (int)Math.Round(_selection.Height * scaleY)));

        Bitmap cropped = ScreenCaptureService.Crop(_capture.Bitmap, pixelRect);
        _accepted = true;
        Close();
        CaptureAccepted?.Invoke(cropped);
        e.Handled = true;
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
        }
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
    }

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

    private void CaptureOverlayWindow_Closed(object? sender, EventArgs e)
    {
        _capture.Dispose();
        if (!_accepted)
        {
            CaptureCancelled?.Invoke();
        }
    }
}
