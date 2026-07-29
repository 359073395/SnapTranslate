using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapTranslate.Models;
using SnapTranslate.Services;
using SnapTranslate.Views;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace SnapTranslate;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GlobalHotkeyService _hotkeyService = new();
    private AppSettings _settings;
    private Key _pendingHotkeyKey = Key.A;
    private ModifierKeys _pendingHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift;
    private bool _captureInProgress;
    private bool _recordingHotkey;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _hotkeyService.Pressed += HotkeyService_Pressed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLanguageOptions();
        ApplySettingsToControls();

        bool initialized = _hotkeyService.Initialize(this);
        bool registered = initialized &&
                          _hotkeyService.TryRegister(
                              _pendingHotkeyKey,
                              _pendingHotkeyModifiers);
        UpdateHotkeyStatus(registered);
    }

    private void LoadLanguageOptions()
    {
        OcrLanguageComboBox.ItemsSource = OcrService.GetAvailableLanguages();
        TargetLanguageComboBox.ItemsSource = AppSettings.TargetLanguages;
    }

    private void ApplySettingsToControls()
    {
        OcrLanguageComboBox.SelectedValue = _settings.OcrLanguage;
        if (OcrLanguageComboBox.SelectedIndex < 0 && OcrLanguageComboBox.Items.Count > 0)
        {
            OcrLanguageComboBox.SelectedIndex = 0;
        }

        TargetLanguageComboBox.SelectedValue = _settings.TargetLanguage;
        TranslationProviderComboBox.SelectedIndex =
            string.Equals(_settings.TranslationProvider, "OpenAI", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        OpenAiEndpointTextBox.Text = _settings.OpenAiEndpoint;
        OpenAiModelTextBox.Text = _settings.OpenAiModel;
        OpenAiApiKeyPasswordBox.Password = _settings.OpenAiApiKey;
        LoadHotkeyFromSettings();
        UpdateProviderPanel();
    }

    private void ReadSettingsFromControls()
    {
        _settings.OcrLanguage =
            OcrLanguageComboBox.SelectedValue?.ToString() ?? OcrService.AutoLanguageTag;
        _settings.TargetLanguage = TargetLanguageComboBox.SelectedValue?.ToString() ?? "zh-CN";
        _settings.TranslationProvider =
            (TranslationProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "GoogleWeb";
        _settings.OpenAiEndpoint = OpenAiEndpointTextBox.Text.Trim();
        _settings.OpenAiModel = OpenAiModelTextBox.Text.Trim();
        _settings.OpenAiApiKey = OpenAiApiKeyPasswordBox.Password.Trim();
        _settings.HotkeyKey = _pendingHotkeyKey.ToString();
        _settings.HotkeyControl = _pendingHotkeyModifiers.HasFlag(ModifierKeys.Control);
        _settings.HotkeyShift = _pendingHotkeyModifiers.HasFlag(ModifierKeys.Shift);
        _settings.HotkeyAlt = _pendingHotkeyModifiers.HasFlag(ModifierKeys.Alt);
        _settings.HotkeyWindows = _pendingHotkeyModifiers.HasFlag(ModifierKeys.Windows);
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        await BeginCaptureAsync();
    }

    private async Task BeginCaptureAsync()
    {
        if (_captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        ReadSettingsFromControls();
        Hide();
        await Task.Delay(180);

        try
        {
            CapturedScreen capture = ScreenCaptureService.CaptureMonitorUnderCursor();
            CaptureOverlayWindow overlay = new(capture);

            overlay.CaptureAccepted += bitmap =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    EditorWindow editor = new(bitmap, _settings);
                    editor.Closed += (_, _) => RestoreMainWindow();
                    editor.Show();
                    editor.Activate();
                });
            };
            overlay.CaptureCancelled += RestoreMainWindow;
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            RestoreMainWindow();
            MessageBox.Show(ex.Message, "无法开始截图", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreMainWindow()
    {
        _captureInProgress = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromControls();
        _settingsService.Save(_settings);
        SettingsStatusText.Text = $"已保存到 {_settingsService.SettingsPath}";
        UpdateHotkeyStatus(
            _hotkeyService.TryRegister(
                _pendingHotkeyKey,
                _pendingHotkeyModifiers),
            saved: true);
    }

    private async void TestOpenAiButton_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromControls();
        TestOpenAiButton.IsEnabled = false;
        OpenAiConnectionStatusText.Text = "正在测试…";
        OpenAiConnectionStatusText.Foreground = Brushes.LightSkyBlue;

        try
        {
            TranslationService translationService = new(_settings);
            string result = await translationService.TestOpenAiConnectionAsync();
            string preview = result.Length <= 80 ? result : $"{result[..80]}…";
            OpenAiConnectionStatusText.Text = $"连接成功：{preview}";
            OpenAiConnectionStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            OpenAiConnectionStatusText.Text = ex.Message;
            OpenAiConnectionStatusText.Foreground = Brushes.Orange;
        }
        finally
        {
            TestOpenAiButton.IsEnabled = true;
        }
    }

    private void LoadHotkeyFromSettings()
    {
        if (!Enum.TryParse(_settings.HotkeyKey, ignoreCase: true, out Key key) ||
            key == Key.None ||
            IsModifierKey(key))
        {
            key = Key.A;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        if (_settings.HotkeyControl)
        {
            modifiers |= ModifierKeys.Control;
        }

        if (_settings.HotkeyShift)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (_settings.HotkeyAlt)
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (_settings.HotkeyWindows)
        {
            modifiers |= ModifierKeys.Windows;
        }

        if (modifiers == ModifierKeys.None)
        {
            modifiers = ModifierKeys.Control | ModifierKeys.Shift;
        }

        _pendingHotkeyKey = key;
        _pendingHotkeyModifiers = modifiers;
        HotkeyTextBox.Text = FormatHotkey(key, modifiers);
    }

    private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recordingHotkey = true;
        HotkeyTextBox.Text = "请按下新的组合键…";
        HotkeyStatusText.Text = "至少包含 Ctrl、Alt、Shift 或 Win 中的一个。";
        HotkeyStatusText.Foreground = Brushes.LightSkyBlue;
    }

    private void HotkeyTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recordingHotkey = false;
        HotkeyTextBox.Text = FormatHotkey(_pendingHotkeyKey, _pendingHotkeyModifiers);
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _recordingHotkey = false;
            Keyboard.ClearFocus();
            UpdateHotkeyStatus(_hotkeyService.IsRegistered);
            return;
        }

        if (IsModifierKey(key))
        {
            HotkeyStatusText.Text = "请在按住修饰键的同时，再按一个字母、数字或功能键。";
            HotkeyStatusText.Foreground = Brushes.LightSkyBlue;
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HotkeyStatusText.Text = "快捷键至少需要 Ctrl、Alt、Shift 或 Win 中的一个。";
            HotkeyStatusText.Foreground = Brushes.Orange;
            return;
        }

        if (!_hotkeyService.TryRegister(key, modifiers))
        {
            HotkeyStatusText.Text =
                $"{FormatHotkey(key, modifiers)} 已被占用，仍使用 {FormatHotkey(_pendingHotkeyKey, _pendingHotkeyModifiers)}。";
            HotkeyStatusText.Foreground = Brushes.Orange;
            return;
        }

        _pendingHotkeyKey = key;
        _pendingHotkeyModifiers = modifiers;
        HotkeyTextBox.Text = FormatHotkey(key, modifiers);
        HotkeyStatusText.Text = "新快捷键已启用；点击“保存设置”可在下次启动时继续使用。";
        HotkeyStatusText.Foreground = Brushes.LightGreen;
        _recordingHotkey = false;
        Keyboard.ClearFocus();
    }

    private void HotkeyService_Pressed(object? sender, EventArgs e)
    {
        if (!_recordingHotkey)
        {
            Dispatcher.InvokeAsync(BeginCaptureAsync);
        }
    }

    private void UpdateHotkeyStatus(bool registered, bool saved = false)
    {
        string hotkey = FormatHotkey(_pendingHotkeyKey, _pendingHotkeyModifiers);
        HotkeyStatusText.Text = registered
            ? saved
                ? $"{hotkey} 已保存并启用。"
                : $"{hotkey} 已启用。"
            : $"{hotkey} 注册失败，可能被其他软件占用；仍可点击“开始截图”。";
        HotkeyStatusText.Foreground = registered ? Brushes.LightGreen : Brushes.Orange;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

    private static string FormatHotkey(Key key, ModifierKeys modifiers)
    {
        List<string> parts = [];
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num {((int)key - (int)Key.NumPad0)}";
        }

        return key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            _ => key.ToString()
        };
    }

    private void TranslationProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateProviderPanel();
    }

    private void UpdateProviderPanel()
    {
        if (OpenAiSettingsPanel is null || TranslationProviderComboBox is null)
        {
            return;
        }

        string? provider = (TranslationProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        OpenAiSettingsPanel.Visibility =
            string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _hotkeyService.Dispose();
    }
}
