using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace SnapTranslate.Services;

public sealed class CapturedScreen : IDisposable
{
    public CapturedScreen(Bitmap bitmap, DrawingRectangle bounds)
    {
        Bitmap = bitmap;
        Bounds = bounds;
        Preview = ScreenCaptureService.ToBitmapSource(bitmap);
    }

    public Bitmap Bitmap { get; }
    public DrawingRectangle Bounds { get; }
    public BitmapSource Preview { get; }

    public void Dispose() => Bitmap.Dispose();
}

public static class ScreenCaptureService
{
    public static CapturedScreen CaptureMonitorUnderCursor()
    {
        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        DrawingRectangle bounds = screen.Bounds;
        Bitmap bitmap = new(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);

        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            bounds.Left,
            bounds.Top,
            0,
            0,
            bounds.Size,
            CopyPixelOperation.SourceCopy);

        return new CapturedScreen(bitmap, bounds);
    }

    public static Bitmap Crop(Bitmap source, Int32Rect requested)
    {
        int x = Math.Clamp(requested.X, 0, Math.Max(0, source.Width - 1));
        int y = Math.Clamp(requested.Y, 0, Math.Max(0, source.Height - 1));
        int width = Math.Clamp(requested.Width, 1, source.Width - x);
        int height = Math.Clamp(requested.Height, 1, source.Height - y);
        return source.Clone(new DrawingRectangle(x, y, width, height), PixelFormat.Format32bppPArgb);
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        nint hBitmap = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}
