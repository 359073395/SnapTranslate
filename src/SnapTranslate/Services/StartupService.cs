using Microsoft.Win32;

namespace SnapTranslate.Services;

public sealed class StartupService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LingxiCapture";

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string executablePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定程序路径。");
                key.SetValue(
                    ValueName,
                    $"\"{executablePath}\" --background",
                    RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }
}
