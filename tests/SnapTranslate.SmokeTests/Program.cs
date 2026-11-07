using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapTranslate;
using SnapTranslate.Controls;
using SnapTranslate.Models;
using SnapTranslate.Services;
using SnapTranslate.Views;
using Bitmap = System.Drawing.Bitmap;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // Do not run App.OnStartup: no settings, hotkey or startup changes in tests.
            App app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            TestIcons();
            TestWindowBranding();
            TestEditing();
            if (!args.Contains("--skip-ocr")) TestOcr();
            if (args.Contains("--clipboard")) TestClipboardAndCancel();
            if (args.Contains("--live-translation")) TestLiveTranslation();
            Check(!typeof(MainWindow).Assembly.GetReferencedAssemblies().Any(
                name => name.Name!.StartsWith("MahApps", StringComparison.Ordinal)), "No full icon pack dependency");
            app.Shutdown();
            Console.WriteLine("All smoke checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void TestIcons()
    {
        foreach (ToolbarIconKind kind in Enum.GetValues<ToolbarIconKind>())
        {
            foreach (int size in new[] { 16, 24, 32 })
            {
                ToolbarIcon icon = new() { Kind = kind, Width = size, Height = size, Foreground = Brushes.White };
                icon.Measure(new Size(size, size));
                icon.Arrange(new Rect(0, 0, size, size));
                RenderTargetBitmap rendered = new(size, size, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(icon);
                byte[] pixels = new byte[size * size * 4];
                rendered.CopyPixels(pixels, size * 4, 0);
                Check(pixels.Any(value => value != 0), $"{kind} visible at {size}px");
            }
        }
    }

    private static void TestWindowBranding()
    {
        MainWindow main = new();
        Version version = typeof(MainWindow).Assembly.GetName().Version!;
        Check(main.Title == $"灵犀截图 v{version.Major}.{version.Minor}.{version.Build}", "Main title identifies version");
        Check(main.Icon is BitmapFrame && main.Icon.Width >= 16, "Main LI window icon resolves");
        _ = new WindowInteropHelper(main).EnsureHandle();
        using Bitmap bitmap = new(320, 180);
        EditorWindow editor = new(new Bitmap(bitmap), new AppSettings());
        Check(editor.Icon is not null, "Advanced editor LI icon resolves");
        TextPromptDialog text = new();
        Check(text.Icon is not null, "Text dialog LI icon resolves");
        text.Close();
        editor.Close();
        Field(main, "_exitRequested", true);
        main.Close();
    }

    private static void TestEditing()
    {
        using Bitmap image = new(800, 450);
        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(image))
        {
            graphics.Clear(System.Drawing.Color.White);
        }
        CaptureOverlayWindow overlay = new(new CapturedScreen(new Bitmap(image), new System.Drawing.Rectangle(0, 0, 800, 450)), new AppSettings());
        FrameworkElement root = (FrameworkElement)overlay.FindName("RootGrid");
        root.Width = 800;
        root.Height = 450;
        root.Measure(new Size(800, 450));
        root.Arrange(new Rect(0, 0, 800, 450));
        root.UpdateLayout();
        Field(overlay, "_selection", new Rect(20, 20, 600, 300));
        Invoke(overlay, "PrepareSelection");
        Check(((FrameworkElement)overlay.FindName("QuickToolbar")).Visibility == Visibility.Visible, "Floating toolbar ready");
        foreach (string tool in new[] { "Rectangle", "Ellipse", "Arrow", "Pen", "Mosaic" })
        {
            Invoke(overlay, tool + "ToolButton_Click", overlay, new RoutedEventArgs());
            Invoke(overlay, "BeginAnnotation", new Point(30, 30));
            Canvas canvas = (Canvas)overlay.FindName("AnnotationCanvas");
            Check(canvas.Children.Count == 1, $"{tool} creates annotation");
            Invoke(overlay, "UndoButton_Click", overlay, new RoutedEventArgs());
            Check(canvas.Children.Count == 0, $"{tool} undo works");
        }
        BitmapSource rendered = (BitmapSource)Invoke(overlay, "RenderComposite")!;
        Check(rendered.PixelWidth > 0 && rendered.PixelHeight > 0, "Final image renders");
        using System.IO.MemoryStream stream = new();
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        encoder.Save(stream);
        Check(stream.Length > 0, "PNG encoding works");
        overlay.Close();
    }

    private static void TestOcr()
    {
        using Bitmap image = new(900, 180);
        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(image))
        using (System.Drawing.Font font = new("Segoe UI", 32))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawString("Lingxi Capture 12345", font, System.Drawing.Brushes.Black, 30, 45);
        }
        OcrRecognitionResult result = Task.Run(
            () => OcrService.RecognizeDetailedAsync(image, "auto")).GetAwaiter().GetResult();
        Console.WriteLine($"Synthetic OCR: {result.RecognizerLanguageTag}; {result.Text}");
        Check(result.Lines.Count > 0 && result.Text.Contains("Capture", StringComparison.OrdinalIgnoreCase),
            "Native Windows automatic OCR recognizes sample");
    }

    private static void TestClipboardAndCancel()
    {
        // Opt-in local check. Keep clipboard values in memory only, never print them.
        DataObject? saved = null;
        var original = Clipboard.GetDataObject();
        if (original is not null)
        {
            saved = new DataObject();
            foreach (string format in original.GetFormats(autoConvert: false))
            {
                object? data = original.GetData(format, autoConvert: false);
                if (data is System.IO.MemoryStream memory) data = new System.IO.MemoryStream(memory.ToArray());
                if (data is not null) saved.SetData(format, data, autoConvert: false);
            }
        }

        try
        {
            CaptureOverlayWindow overlay = CreateTestOverlay();
            Field(overlay, "_selection", new Rect(20, 20, 400, 200));
            Invoke(overlay, "PrepareSelection");
            bool completed = false;
            bool cancelled = false;
            overlay.CaptureCompleted += () => completed = true;
            overlay.CaptureCancelled += () => cancelled = true;
            InvokeKey(overlay, System.Windows.Input.Key.Return);
            BitmapSource? image = Clipboard.GetImage();
            Check(completed && !cancelled && image is not null && image.PixelWidth == 400 && image.PixelHeight == 200,
                "Enter finishes capture and copies native clipboard image");

            CaptureOverlayWindow cancelOverlay = CreateTestOverlay();
            bool escaped = false;
            cancelOverlay.CaptureCancelled += () => escaped = true;
            InvokeKey(cancelOverlay, System.Windows.Input.Key.Escape);
            BitmapSource? unchanged = Clipboard.GetImage();
            Check(escaped && unchanged is not null && unchanged.PixelWidth == 400 && unchanged.PixelHeight == 200,
                "Escape cancels without replacing clipboard image");
        }
        finally
        {
            if (saved is not null) Clipboard.SetDataObject(saved, copy: true);
            else Clipboard.Clear();
        }
    }

    private static CaptureOverlayWindow CreateTestOverlay()
    {
        Bitmap bitmap = new(800, 450);
        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.Clear(System.Drawing.Color.White);
        CaptureOverlayWindow overlay = new(new CapturedScreen(bitmap,
            new System.Drawing.Rectangle(0, 0, 800, 450)), new AppSettings());
        FrameworkElement root = (FrameworkElement)overlay.FindName("RootGrid");
        root.Width = 800;
        root.Height = 450;
        root.Measure(new Size(800, 450));
        root.Arrange(new Rect(0, 0, 800, 450));
        root.UpdateLayout();
        return overlay;
    }

    private static void InvokeKey(CaptureOverlayWindow overlay, System.Windows.Input.Key key)
    {
        nint handle = new WindowInteropHelper(overlay).EnsureHandle();
        var arguments = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, HwndSource.FromHwnd(handle), Environment.TickCount, key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
        };
        Invoke(overlay, "Window_KeyDown", overlay, arguments);
        Check(arguments.Handled, $"{key} handled by screenshot window");
    }

    private static void TestLiveTranslation()
    {
        try
        {
            AppSettings settings = new SettingsService().Load();
            TranslationService service = new(settings);
            string[] lines = ["欢迎来到我们的商店", "今天分享一个实用技巧", "谢谢观看"];
            Stopwatch timer = Stopwatch.StartNew();
            IReadOnlyList<string> translated = Task.Run(() => service.TranslateLinesAsync(lines)).GetAwaiter().GetResult();
            timer.Stop();
            Check(translated.Count == lines.Length && translated.All(line => !string.IsNullOrWhiteSpace(line)),
                "Live synthetic three-line translation returns aligned non-empty results");
            Console.WriteLine($"Live synthetic translation elapsed: {timer.ElapsedMilliseconds} ms");
            IReadOnlyList<string> cached = Task.Run(() => service.TranslateLinesAsync(lines)).GetAwaiter().GetResult();
            Check(cached.SequenceEqual(translated), "Translation cache returns the same results");
        }
        catch
        {
            // Remote errors may contain gateway details; do not log settings or response bodies.
            throw new InvalidOperationException("Live translation failed; details omitted to protect local API settings.");
        }
    }

    private static void Field(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static object? Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
    }
}
