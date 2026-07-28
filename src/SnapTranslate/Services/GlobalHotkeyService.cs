using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SnapTranslate.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5354;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkA = 0x41;

    private HwndSource? _source;
    private nint _windowHandle;
    private bool _registered;

    public event EventHandler? Pressed;

    public bool Register(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == nint.Zero)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowMessageHook);
        _registered = RegisterHotKey(_windowHandle, HotkeyId, ModControl | ModShift, VkA);
        return _registered;
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }

        _source?.RemoveHook(WindowMessageHook);
        _source = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
