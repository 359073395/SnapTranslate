using System.Windows;
using System.Windows.Controls;
using SnapTranslate.Models;
using SnapTranslate.Services;
using SnapTranslate.Views;
using MessageBox = System.Windows.MessageBox;

namespace SnapTranslate;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GlobalHotkeyService _hotkeyService = new();
    private AppSettings _settings;
    private bool _captureInProgress;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        _hotkeyService.Pressed += (_, _) => Dispatcher.InvokeAsync(BeginCaptureAsync);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLanguageOptions();
        ApplySettingsToControls();

        bool registered = _hotkeyService.Register(this);
        HotkeyStatusText.Text = registered
            ? "快捷键已启用"
            : "快捷键注册失败，可能被其他软件占用";
        HotkeyStatusText.Foreground = registered
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Orange;
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
        UpdateProviderPanel();
    }

    private void ReadSettingsFromControls()
    {
        _settings.OcrLanguage = OcrLanguageComboBox.SelectedValue?.ToString() ?? "en";
        _settings.TargetLanguage = TargetLanguageComboBox.SelectedValue?.ToString() ?? "zh-CN";
        _settings.TranslationProvider =
            (TranslationProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "GoogleWeb";
        _settings.OpenAiEndpoint = OpenAiEndpointTextBox.Text.Trim();
        _settings.OpenAiModel = OpenAiModelTextBox.Text.Trim();
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
