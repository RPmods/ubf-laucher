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
        _showingUserPanel = true;
        UserPanel.Visibility = Visibility.Visible;
        UserNameInput.Focus();
        UserNameInput.SelectAll();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(850))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        AnimateUserIn();
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
                Width = name switch { "YouTube" or "Twitch" => 112, "Discord" => 106, "TikTok" => 98, _ => 86 },
                Height = 38,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = validUrl,
                Style = (Style)FindResource("QuietButton")
            };

            var (startColor, endColor, borderColor) = name switch
            {
                "Discord" => (System.Windows.Media.Color.FromRgb(91, 79, 190), System.Windows.Media.Color.FromRgb(45, 40, 102), System.Windows.Media.Color.FromRgb(157, 145, 255)),
                "YouTube" => (System.Windows.Media.Color.FromRgb(224, 47, 71), System.Windows.Media.Color.FromRgb(103, 24, 39), System.Windows.Media.Color.FromRgb(255, 137, 151)),
                "Twitch" => (System.Windows.Media.Color.FromRgb(134, 73, 220), System.Windows.Media.Color.FromRgb(67, 36, 119), System.Windows.Media.Color.FromRgb(194, 153, 255)),
                "TikTok" => (System.Windows.Media.Color.FromRgb(39, 79, 85), System.Windows.Media.Color.FromRgb(22, 36, 45), System.Windows.Media.Color.FromRgb(114, 222, 214)),
                _ => (System.Windows.Media.Color.FromRgb(54, 82, 98), System.Windows.Media.Color.FromRgb(29, 41, 54), System.Windows.Media.Color.FromRgb(133, 177, 200))
            };
            button.Background = new System.Windows.Media.LinearGradientBrush(startColor, endColor, 40);
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(borderColor);
            button.BorderThickness = new Thickness(1);
            button.FontSize = 11;
            button.FontWeight = FontWeights.SemiBold;

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(new System.Windows.Controls.Image
            {
                Source = (System.Windows.Media.ImageSource)FindResource($"{name}Mark"),
                Width = 17,
                Height = 17,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = name.ToUpperInvariant(),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            button.Content = content;
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
        VerifyButton.Content = UiText.Get(language, "verify");
        FolderButton.Content = UiText.Get(language, "folder");
        ChangeUserButton.Content = UiText.Get(language, "user");
        LauncherUpdateButton.Content = UiText.Get(language, "launcher_update");
        SettingsButton.ToolTip = UiText.Get(language, "settings");
        VolumePopupToggle.ToolTip = UiText.Get(language, "sound");
        MuteButton.Content = UiText.Get(language, "mute");
        UpdateAudioControls();
        var launcherVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.7";
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
            _ = _viewModel.CheckGamePresenceAsync();
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
        BackgroundOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(520)));
        GamePanel.Visibility = Visibility.Visible;
        AnimateGameIn();
        await _viewModel.CheckGamePresenceAsync();
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
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
                await _viewModel.CheckGamePresenceAsync();
                break;
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
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
            _ = _viewModel.CheckGamePresenceAsync();
        }
    }

    private void ChangeUser_Click(object sender, RoutedEventArgs e)
    {
        if (_showingUserPanel) return;
        _showingUserPanel = true;
        _viewModel.ChangeUserName();
        NameError.Text = "";
        BackgroundOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(420)));
        UserPanel.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        GamePanel.BeginAnimation(OpacityProperty, fade);
        var slideOut = new DoubleAnimation(34, TimeSpan.FromMilliseconds(180));
        GameTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
        AnimateUserIn();
        UserNameInput.Focus();
        UserNameInput.SelectAll();
    }

    private async Task AnimateUserOutAsync()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190));
        UserPanel.BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(-65, TimeSpan.FromMilliseconds(190));
        UserTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        await Task.Delay(200);
        UserPanel.Visibility = Visibility.Collapsed;
    }

    private void AnimateUserIn()
    {
        UserTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UserPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)));
    }

    private void AnimateGameIn()
    {
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
        if (_gameProcess is not null)
        {
            try { if (!_gameProcess.HasExited) return; }
            catch (InvalidOperationException) { }
            RestoreAfterGame(_gameProcess);
        }

        _audio.Pause();
        UpdateAudioControls();
        var process = _viewModel.LaunchGame();
        if (process is null)
        {
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
                _audio.Resume();
                UpdateAudioControls();
                return;
            }

            ShowInTaskbar = false;
            Hide();
            _gameWindowHidden = true;
            if (!SetForegroundWindow(gameWindow))
                _logger.Info("Windows did not allow UBF to take foreground focus after startup.");
        }
        catch (Exception ex)
        {
            _logger.Error("Could not wait for the UBF window", ex);
            _audio.Resume();
            UpdateAudioControls();
            _viewModel.NotifyGameWindowUnavailable();
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
