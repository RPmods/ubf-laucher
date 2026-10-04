using System.Windows;
using System.Windows.Controls;
using UBFLauncher.Models;
using UBFLauncher.Services;
using Microsoft.Win32;

namespace UBFLauncher;

public partial class SettingsWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly MainViewModel _viewModel;
    private readonly Logger _logger;

    public SettingsWindow(LauncherConfig config, MainViewModel viewModel, Logger logger)
    {
        InitializeComponent();
        _config = config;
        _viewModel = viewModel;
        _logger = logger;
        InstallPath.Text = config.InstallDirectory;
        AutomaticUpdates.IsChecked = config.AutomaticLauncherUpdates;
        LanguagePicker.SelectedValue = config.Language;
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.16";
        AboutBody.Text = $"{UiText.Get(config.Language, "about_body")}\nVersión {version} · {Environment.OSVersion.VersionString}";
        ApplyLanguage(config.Language);
    }

    private string SelectedLanguage => LanguagePicker.SelectedValue as string ?? _config.Language;

    private void ApplyLanguage(string language)
    {
        TitleText.Text = UiText.Get(language, "settings_title");
        SubtitleText.Text = UiText.Get(language, "settings_subtitle");
        AboutHeading.Text = UiText.Get(language, "about_title");
        LanguageLabel.Text = UiText.Get(language, "language");
        PathLabel.Text = UiText.Get(language, "install_path");
        BrowseButton.Content = UiText.Get(language, "browse");
        AutomaticUpdates.Content = UiText.Get(language, "automatic_updates");
        AutomaticHint.Text = UiText.Get(language, "automatic_updates_hint");
        InstallationHeading.Text = language == "en" ? "INSTALLATION & UPDATES" : "INSTALACIÓN Y ACTUALIZACIONES";
        LanguageHint.Text = language == "en" ? "Choose how the launcher text is displayed." : "Elige cómo se muestran los textos.";
        MaintenanceHeading.Text = language == "en" ? "MAINTENANCE" : "MANTENIMIENTO";
        MaintenanceHint.Text = language == "en" ? "Review local files and launcher updates." : "Revisa los archivos locales y las novedades del launcher.";
        VerifyGameButton.Content = UiText.Get(language, "verify_game");
        CheckUpdateButton.Content = UiText.Get(language, "check_launcher");
        SaveButton.Content = UiText.Get(language, "save_settings");
        CancelButton.Content = UiText.Get(language, "cancel");
        Title = UiText.Get(language, "settings_title");
        if (AboutBody is not null)
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.16";
            AboutBody.Text = $"{UiText.Get(language, "about_body")}\n{(language == "en" ? "Version" : "Versión")} {version} · {Environment.OSVersion.VersionString}";
        }
    }

    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker?.SelectedValue is string language) ApplyLanguage(language);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiText.Get(SelectedLanguage, "pick_game_folder"),
            InitialDirectory = Directory.Exists(InstallPath.Text) ? InstallPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) InstallPath.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings()) return;
        _viewModel.ApplyLanguage(_config.Language);
        DialogResult = true;
    }

    private bool SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(InstallPath.Text))
        {
            MessageBox.Show(this, UiText.Get(SelectedLanguage, "path_required"), UiText.Get(SelectedLanguage, "settings_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _config.InstallDirectory = Path.GetFullPath(InstallPath.Text.Trim());
        _config.Language = SelectedLanguage;
        _config.AutomaticLauncherUpdates = AutomaticUpdates.IsChecked == true;
        ConfigurationService.Save(_config, _logger);
        return true;
    }

    private async void VerifyGame_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings()) return;
        _viewModel.ApplyLanguage(_config.Language);
        Close();
        if (File.Exists(Path.Combine(_config.InstallDirectory, _config.GameExecutableName)))
            await _viewModel.VerifyGameAsync();
        else
            await _viewModel.CheckGamePresenceAsync();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateResult.Text = UiText.Get(SelectedLanguage, "checking_update");
        try
        {
            var resultKey = await _viewModel.CheckLauncherUpdateAsync();
            UpdateResult.Text = UiText.Get(SelectedLanguage, resultKey);
        }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
