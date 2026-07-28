using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnapTranslate.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5354;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private nint _windowHandle;
    private bool _registered;
    private Key _registeredKey;
    private ModifierKeys _registeredModifiers;

    public event EventHandler? Pressed;
    public bool IsRegistered => _registered;

    public bool Initialize(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == nint.Zero)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowMessageHook);
        return _source is not null;
    }

    public bool TryRegister(Key key, ModifierKeys modifiers)
    {
        if (_windowHandle == nint.Zero || _source is null || key == Key.None)
        {
            return false;
        }

        if (_registered && key == _registeredKey && modifiers == _registeredModifiers)
        {
            return true;
        }

        Key previousKey = _registeredKey;
        ModifierKeys previousModifiers = _registeredModifiers;
        bool hadPreviousRegistration = _registered;

        Unregister();

        uint virtualKey = unchecked((uint)KeyInterop.VirtualKeyFromKey(key));
        _registered = RegisterHotKey(
            _windowHandle,
            HotkeyId,
            ToNativeModifiers(modifiers) | ModNoRepeat,
            virtualKey);

        if (_registered)
        {
            _registeredKey = key;
            _registeredModifiers = modifiers;
            return true;
        }

        if (hadPreviousRegistration)
        {
            uint previousVirtualKey = unchecked((uint)KeyInterop.VirtualKeyFromKey(previousKey));
            _registered = RegisterHotKey(
                _windowHandle,
                HotkeyId,
                ToNativeModifiers(previousModifiers) | ModNoRepeat,
                previousVirtualKey);
            if (_registered)
            {
                _registeredKey = previousKey;
                _registeredModifiers = previousModifiers;
            }
        }

        return false;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint nativeModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            nativeModifiers |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            nativeModifiers |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            nativeModifiers |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            nativeModifiers |= ModWindows;
        }

        return nativeModifiers;
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

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
