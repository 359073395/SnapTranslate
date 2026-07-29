using System.Threading;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SnapTranslate;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName =
        @"Local\LingxiCapture.SingleInstance.v1";
    private const string ShowWindowEventName =
        @"Local\LingxiCapture.ShowWindow.v1";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private CancellationTokenSource? _signalCancellation;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "灵犀截图遇到错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using EventWaitHandle showWindowEvent =
                    EventWaitHandle.OpenExisting(ShowWindowEventName);
                showWindowEvent.Set();
            }
            catch
            {
                MessageBox.Show(
                    "灵犀截图已经在运行，请从系统托盘打开。",
                    "灵犀截图",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        _showWindowEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowWindowEventName);
        _signalCancellation = new CancellationTokenSource();

        bool startHidden = e.Args.Any(
            argument => string.Equals(
                argument,
                "--background",
                StringComparison.OrdinalIgnoreCase));
        _mainWindow = new MainWindow(startHidden);
        MainWindow = _mainWindow;
        _mainWindow.Show();

        _ = ListenForShowWindowSignalAsync(_signalCancellation.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalCancellation?.Cancel();
        _showWindowEvent?.Set();
        _showWindowEvent?.Dispose();
        _signalCancellation?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex may already have been released during shutdown.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private Task ListenForShowWindowSignalAsync(CancellationToken cancellationToken)
    {
        if (_showWindowEvent is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            WaitHandle[] handles =
            [
                _showWindowEvent,
                cancellationToken.WaitHandle
            ];
            while (!cancellationToken.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(handles);
                if (signaled != 0 || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Dispatcher.InvokeAsync(() => _mainWindow?.ShowFromTray());
            }
        }, cancellationToken);
    }
}
