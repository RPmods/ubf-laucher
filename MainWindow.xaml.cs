using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using UBFLauncher.Models;
using UBFLauncher.Services;
using Microsoft.Win32;

namespace UBFLauncher;

public partial class MainWindow : Window
{
    private const double GameOverlayOpacity = 0.12;
    private const double ProfileOverlayOpacity = 0.35;

    [DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly MainViewModel _viewModel;
    private readonly AudioService _audio;
    private readonly LauncherConfig _config;
    private readonly Logger _logger;
    private bool _controlsReady;
    private bool _showingUserPanel;
    private Process? _gameProcess;
    private bool _gameWindowHidden;
    private bool _launchInProgress;

    public MainWindow(MainViewModel viewModel, AudioService audio, LauncherConfig config, Logger logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _audio = audio;
        _config = config;
        _logger = logger;
        _audio.VolumeChanged += Audio_VolumeChanged;
        DataContext = viewModel;
        _viewModel.UpdateHandoffRequested += (_, _) => Application.Current.Shutdown();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.LauncherUpdateAvailable))
                LauncherUpdateButton.Visibility = _viewModel.LauncherUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _audio.Start();
        VolumeSlider.Value = _audio.Volume;
        _controlsReady = true;
        UpdateAudioControls();
        ApplyLanguage();
        LoadBackgroundVideo();
        LoadLogo();
        BuildSocialLinks();
        await _viewModel.InitializeAsync();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(850))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        if (_viewModel.HasUserName)
        {
            _showingUserPanel = false;
            BackgroundOverlay.Opacity = GameOverlayOpacity;
            GamePanel.Visibility = Visibility.Visible;
            GamePanel.IsHitTestVisible = true;
            AnimateGameIn();
        }
        else
        {
            _showingUserPanel = true;
            BackgroundOverlay.Opacity = ProfileOverlayOpacity;
            UserPanel.Visibility = Visibility.Visible;
            UserPanel.IsHitTestVisible = true;
            UserNameInput.Focus();
            UserNameInput.SelectAll();
            AnimateUserIn();
        }
    }

    private void LoadBackgroundVideo()
    {
        try
        {
            var path = ResolveAsset(_config.BackgroundVideoPath);
            if (!File.Exists(path)) { _logger.Info($"Background video not found: {path}"); return; }
            BackgroundVideo.Source = new Uri(path);
            BackgroundVideo.Play();
        }
        catch (Exception ex) { _logger.Error("Could not start background video", ex); FallbackBackground.Visibility = Visibility.Visible; }
    }

    private void LoadLogo()
    {
        try
        {
            var path = ResolveAsset(_config.LogoPath);
            if (!File.Exists(path)) return;
            var logo = new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
            GameLogo.Source = logo;
            LauncherBrandIcon.Source = logo;
            GameLogo.Visibility = Visibility.Visible;
            Wordmark.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { _logger.Error("Could not load game logo", ex); }
    }

    private void BuildSocialLinks()
    {
        foreach (var name in new[] { "Discord", "YouTube", "Twitch", "TikTok", "Web" })
        {
            _config.SocialLinks.TryGetValue(name, out var url);
            var validUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
            var button = new Button
            {
                ToolTip = validUrl
                    ? (name == "Twitch" ? "twitch.tv/rodrigorpmods" : uri!.Host)
                    : $"Configura el enlace de {name} en launcher.settings.json",
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = validUrl,
                Style = (Style)FindResource("QuietButton")
            };

            button.Background = System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.FontSize = 10;
            button.FontWeight = FontWeights.SemiBold;

            button.Content = new System.Windows.Controls.Image
            {
                Source = (System.Windows.Media.ImageSource)FindResource($"{name}Mark"),
                Width = 16,
                Height = 16,
                Stretch = System.Windows.Media.Stretch.Uniform
            };
            button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"Abrir {name}");

            if (validUrl) button.Click += (_, _) => OpenSocialLink(url!);
            SocialButtons.Children.Add(button);
        }
    }
    private void ApplyLanguage()
    {
        var language = _config.Language;
        WelcomeTitle.Text = UiText.Get(language, "welcome");
        UsernameLabel.Text = UiText.Get(language, "username_label");
        ContinueButton.Content = UiText.Get(language, "continue");
        UsernameHint.Text = UiText.Get(language, "username_saved");
        GameSectionLabel.Text = UiText.Get(language, "game_section");
        InstalledVersionLabel.Text = UiText.Get(language, "installed_version");
        AvailableVersionLabel.Text = UiText.Get(language, "available_version");
        VerifyButton.Content = UiText.Get(language, "verify");
        FolderButton.Content = UiText.Get(language, "folder");
        ChangeUserButton.Content = UiText.Get(language, "user");
        LauncherUpdateButton.Content = UiText.Get(language, "launcher_update");
        SettingsButton.ToolTip = UiText.Get(language, "settings");
        VolumePopupToggle.ToolTip = UiText.Get(language, "sound");
        MuteButton.Content = UiText.Get(language, "mute");
        UpdateAudioControls();
        var launcherVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.14";
        LauncherBadge.Text = $"UBF  /  LAUNCHER  ·  V{launcherVersion}";
    }

    private void Audio_VolumeChanged(object? sender, EventArgs e) => UpdateAudioControls();

    private void UpdateAudioControls()
    {
        if (VolumeIcon is null) return;
        VolumeIcon.Source = (System.Windows.Media.ImageSource)FindResource(_audio.IsMuted ? "AudioMutedMark" : "AudioOnMark");
        VolumePopupToggle.ToolTip = _audio.IsPaused
            ? UiText.Get(_config.Language, "audio_paused")
            : UiText.Get(_config.Language, "sound");
        MuteButton.Content = UiText.Get(_config.Language, _audio.IsMuted ? "unmute" : "mute");
        MuteButton.IsEnabled = !_audio.IsPaused;
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var previousLanguage = _config.Language;
        var previousInstallDirectory = _config.InstallDirectory;
        var settings = new SettingsWindow(_config, _viewModel, _logger) { Owner = this };
        var saved = settings.ShowDialog() == true;
        if (!previousLanguage.Equals(_config.Language, StringComparison.OrdinalIgnoreCase)) ApplyLanguage();
        if (saved && !previousInstallDirectory.Equals(_config.InstallDirectory, StringComparison.OrdinalIgnoreCase))
            _ = _viewModel.RefreshUpdatesAsync();
        LauncherUpdateButton.Visibility = _viewModel.LauncherUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSocialLink(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { _logger.Error("Could not open social link", ex); }
        }
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.SaveUserName()) { NameError.Text = _viewModel.Details; UserNameInput.Focus(); return; }
        NameError.Text = "";
        _showingUserPanel = false;
        await AnimateUserOutAsync();
        BackgroundOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(GameOverlayOpacity, TimeSpan.FromMilliseconds(360)));
        GamePanel.Visibility = Visibility.Visible;
        GamePanel.IsHitTestVisible = true;
        AnimateGameIn();
        await _viewModel.RefreshUpdatesAsync();
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_launchInProgress || _viewModel.State == LauncherState.Launching) return;
        switch (_viewModel.State)
        {
            case LauncherState.NotInstalled:
                var folder = new OpenFolderDialog
                {
                Title = UiText.Get(_config.Language, "pick_game_folder"),
                InitialDirectory = Directory.Exists(_viewModel.InstallDirectory)
                    ? _viewModel.InstallDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Multiselect = false
                };
                if (folder.ShowDialog(this) == true) await _viewModel.InstallOrUpdateGameAsync(folder.FolderName);
                break;
            case LauncherState.CheckingGame:
                await _viewModel.VerifyGameAsync();
                break;
            case LauncherState.Repairing:
            case LauncherState.Updating:
                await _viewModel.InstallOrUpdateGameAsync();
                break;
            case LauncherState.ReadyToPlay:
                await StartGameProcessAsync();
                break;
            case LauncherState.Error:
                await _viewModel.RetryLastOperationAsync();
                break;
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_launchInProgress || _viewModel.IsBusy) return;
        if (_viewModel.State == LauncherState.NotInstalled)
        {
            await Primary_ClickAsync();
            return;
        }
        await _viewModel.VerifyGameAsync();
    }

    private async Task Primary_ClickAsync()
    {
        var dialog = new OpenFolderDialog { Title = UiText.Get(_config.Language, "pick_game_folder"), Multiselect = false };
        if (dialog.ShowDialog(this) == true) await _viewModel.InstallOrUpdateGameAsync(dialog.FolderName);
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = UiText.Get(_config.Language, "install_path"), InitialDirectory = _viewModel.InstallDirectory, Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            _config.InstallDirectory = dialog.FolderName;
            ConfigurationService.Save(_config, _logger);
            _ = _viewModel.RefreshUpdatesAsync();
        }
    }

    private async void ChangeUser_Click(object sender, RoutedEventArgs e)
    {
        if (_showingUserPanel || _viewModel.IsBusy) return;
        _showingUserPanel = true;
        _viewModel.ChangeUserName();
        NameError.Text = "";
        GamePanel.IsHitTestVisible = false;
        BackgroundOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(ProfileOverlayOpacity, TimeSpan.FromMilliseconds(300)));
        await AnimateGameOutAsync();
        UserPanel.Visibility = Visibility.Visible;
        UserPanel.IsHitTestVisible = true;
        AnimateUserIn();
        UserNameInput.Focus();
        UserNameInput.SelectAll();
    }

    private async Task AnimateUserOutAsync()
    {
        UserPanel.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190));
        UserPanel.BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(-65, TimeSpan.FromMilliseconds(190));
        UserTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        await Task.Delay(200);
        UserPanel.Visibility = Visibility.Collapsed;
    }

    private async Task AnimateGameOutAsync()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        GamePanel.BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(34, TimeSpan.FromMilliseconds(180));
        GameTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        await Task.Delay(190);
        GamePanel.Visibility = Visibility.Collapsed;
    }

    private void AnimateUserIn()
    {
        UserPanel.BeginAnimation(OpacityProperty, null);
        UserTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        UserPanel.Opacity = 0;
        UserTransform.X = 34;
        UserTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UserPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)));
    }

    private void AnimateGameIn()
    {
        GamePanel.BeginAnimation(OpacityProperty, null);
        GameTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        GamePanel.Opacity = 0;
        GameTransform.X = 34;
        GameTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        GamePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(480)));
    }

    private async void LauncherUpdate_Click(object sender, RoutedEventArgs e) => await _viewModel.UpdateLauncherAsync();
    private async void UserNameInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Continue_Click(sender, new RoutedEventArgs()); await Task.CompletedTask; }
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_controlsReady) _viewModel.SaveMusicVolume(e.NewValue); }
    private void Mute_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleMusicMute();
    private void VolumeToggle_Checked(object sender, RoutedEventArgs e) => VolumePopup.IsOpen = true;
    private void VolumeToggle_Unchecked(object sender, RoutedEventArgs e) => VolumePopup.IsOpen = false;
    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e) { BackgroundVideo.Position = TimeSpan.Zero; BackgroundVideo.Play(); }
    private void BackgroundVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) { _logger.Error("Background video playback failed", e.ErrorException); BackgroundVideo.Visibility = Visibility.Collapsed; }
    private async Task StartGameProcessAsync()
    {
        if (_launchInProgress) return;
        if (_gameProcess is not null)
        {
            try { if (!_gameProcess.HasExited) return; }
            catch (InvalidOperationException) { }
            RestoreAfterGame(_gameProcess);
        }

        _launchInProgress = true;
        _audio.Pause();
        UpdateAudioControls();
        var process = _viewModel.LaunchGame();
        if (process is null)
        {
            _launchInProgress = false;
            _audio.Resume();
            UpdateAudioControls();
            return;
        }

        _gameProcess = process;
        process.Exited += GameProcess_Exited;

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            IntPtr gameWindow = IntPtr.Zero;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(120);
                process.Refresh();
                if (process.HasExited)
                {
                    RestoreAfterGame(process);
                    return;
                }

                gameWindow = process.MainWindowHandle;
                if (gameWindow != IntPtr.Zero) break;
            }

            if (gameWindow == IntPtr.Zero)
            {
                _logger.Error("UBF stayed running but did not create a main window within 30 seconds.");
                _viewModel.NotifyGameWindowUnavailable();
                return;
            }

            ShowInTaskbar = false;
            Hide();
            _gameWindowHidden = true;
            _viewModel.NotifyGameWindowStarted();
            if (!SetForegroundWindow(gameWindow))
                _logger.Info("Windows did not allow UBF to take foreground focus after startup.");
        }
        catch (Exception ex)
        {
            _logger.Error("Could not wait for the UBF window", ex);
            try
            {
                if (!process.HasExited)
                {
                    _viewModel.NotifyGameWindowUnavailable();
                    return;
                }
            }
            catch (InvalidOperationException) { }
            RestoreAfterGame(process);
        }
    }
    private void GameProcess_Exited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;
        Dispatcher.BeginInvoke(() => RestoreAfterGame(process));
    }

    private void RestoreAfterGame(Process process)
    {
        if (!ReferenceEquals(_gameProcess, process))
        {
            process.Dispose();
            return;
        }

        _gameProcess = null;
        _launchInProgress = false;
        process.Exited -= GameProcess_Exited;
        try
        {
            if (process.HasExited) _logger.Info($"UBF exited with code {process.ExitCode}");
        }
        catch (InvalidOperationException) { }
        process.Dispose();

        var gameWindowWasShown = _gameWindowHidden;
        if (gameWindowWasShown)
        {
            _gameWindowHidden = false;
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        _audio.Resume();
        UpdateAudioControls();
        if (gameWindowWasShown) _viewModel.NotifyGameProcessExited();
        else _viewModel.NotifyGameStartupFailed();
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 1) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ConfigurationService.Save(_config, _logger);
        _audio.Dispose();
        BackgroundVideo.Stop();
        if (_gameProcess is not null)
        {
            _gameProcess.Exited -= GameProcess_Exited;
            _gameProcess.Dispose();
            _gameProcess = null;
        }
    }
    private static string ResolveAsset(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
