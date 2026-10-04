using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using UBFLauncher.Models;
using UBFLauncher.Services;

namespace UBFLauncher;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly LauncherConfig _config;
    private readonly IDistributionService _distribution;
    private readonly GameVerifier _verifier;
    private readonly GameInstaller _installer;
    private readonly AudioService _audio;
    private readonly Logger _logger;
    private LauncherState _state = LauncherState.CheckingLauncherUpdate;
    private string _status = "Preparando UBF...";
    private string _details = "";
    private string _userName = "";
    private string _progressText = "";
    private double _progress;
    private bool _busy;
    private bool _progressIsIndeterminate;
    private bool _launcherUpdateAvailable;
    private string _currentFile = "";
    private string _availableGameVersion = "";
    private string _availableLauncherVersion = "";
    private RetryOperation _retryOperation = RetryOperation.Verify;
    private GameManifest? _manifest;
    private IReadOnlyList<GameFile> _invalidFiles = [];
    private string _language;

    public MainViewModel(LauncherConfig config, IDistributionService distribution, GameVerifier verifier,
        GameInstaller installer, AudioService audio, Logger logger)
    {
        _config = config;
        _distribution = distribution;
        _verifier = verifier;
        _installer = installer;
        _audio = audio;
        _logger = logger;
        _userName = config.UserName ?? "";
        _language = config.Language;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? UpdateHandoffRequested;
    public LauncherState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PrimaryAction));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(CanUsePrimaryAction));
            OnPropertyChanged(nameof(CanVerify));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string Details { get => _details; private set { _details = value; OnPropertyChanged(); } }
    public string UserName { get => _userName; set { _userName = value; OnPropertyChanged(); } }
    public string CurrentFile { get => _currentFile; private set { _currentFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsProgressIndeterminate)); } }
    public double Progress { get => _progress; private set { _progress = value; OnPropertyChanged(); } }
    public string ProgressText { get => _progressText; private set { _progressText = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUsePrimaryAction)); OnPropertyChanged(nameof(CanVerify)); OnPropertyChanged(nameof(ShowOperationProgress)); OnPropertyChanged(nameof(IsProgressIndeterminate)); } }
    public bool LauncherUpdateAvailable { get => _launcherUpdateAvailable; private set { _launcherUpdateAvailable = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLauncherUpdate)); } }
    public bool IsReady => State == LauncherState.ReadyToPlay;
    public bool CanUsePrimaryAction => !IsBusy && State != LauncherState.Launching;
    public bool CanVerify => !IsBusy && State != LauncherState.Launching;
    public bool HasUserName => !string.IsNullOrWhiteSpace(_config.UserName);
    public string PrimaryAction => State switch
    {
        LauncherState.NotInstalled => UiText.Get(_language, "action_install"),
        LauncherState.CheckingGame => UiText.Get(_language, "action_verify"),
        LauncherState.Repairing => UiText.Get(_language, "action_repair"),
        LauncherState.Updating => UiText.Get(_language, "action_update"),
        LauncherState.ReadyToPlay => UiText.Get(_language, "action_play"),
        LauncherState.Launching => UiText.Get(_language, "action_launching"),
        LauncherState.Error => UiText.Get(_language, "action_retry"),
        _ => "..."
    };
    public string InstallDirectory => _config.InstallDirectory;
    public IReadOnlyDictionary<string, string> SocialLinks => _config.SocialLinks;
    public double MusicVolume => _audio.Volume;
    public bool ShowOperationProgress => IsBusy;
    public bool IsProgressIndeterminate => IsBusy && _progressIsIndeterminate;
    public string InstalledGameVersion => HasLocalGameExecutable
        ? string.IsNullOrWhiteSpace(_config.InstalledGameVersion) ? UiText.Get(_language, "version_unknown") : _config.InstalledGameVersion
        : UiText.Get(_language, "version_not_installed");
    public string AvailableGameVersion => _availableGameVersion;
    public bool HasGameUpdate => !string.IsNullOrWhiteSpace(_availableGameVersion) &&
        (!HasLocalGameExecutable || !string.Equals(_availableGameVersion, _config.InstalledGameVersion, StringComparison.OrdinalIgnoreCase));
    public string LauncherInstalledVersion => GetCurrentLauncherVersion();
    public string AvailableLauncherVersion => _availableLauncherVersion;
    public bool HasLauncherUpdate => LauncherUpdateAvailable && !string.IsNullOrWhiteSpace(_availableLauncherVersion);

    public Task InitializeAsync()
    {
        if (!HasUserName)
        {
            State = LauncherState.EnteringUsername;
            Status = UiText.Get(_language, "welcome");
            _ = CheckLauncherUpdateAsync();
            return Task.CompletedTask;
        }

        _ = RefreshUpdatesAsync();
        return Task.CompletedTask;
    }

    public async Task RefreshUpdatesAsync()
    {
        await VerifyGameAsync();
        await CheckLauncherUpdateAsync(allowAutoInstall: true);
    }

    public bool SaveUserName()
    {
        var cleaned = UserName.Trim();
        if (cleaned.Length is < 1 or > 24 || cleaned.Any(char.IsControl))
        {
            Details = UiText.Get(_language, "name_invalid");
            return false;
        }
        UserName = cleaned;
        _config.UserName = cleaned;
        ConfigurationService.Save(_config, _logger);
        _logger.Info("Player name saved");
        return true;
    }

    public async Task CheckGamePresenceAsync()
    {
        try
        {
            State = LauncherState.CheckingGame;
            Status = UiText.Get(_language, "checking_game");
            Details = "";
            if (IsUnrealProjectOutputDirectory(_config.InstallDirectory))
            {
                State = LauncherState.NotInstalled;
                Status = UiText.Get(_language, "invalid_install_directory");
                Details = UiText.Get(_language, "install_package_root");
                return;
            }
            if (!HasLocalGameExecutable)
            {
                State = LauncherState.NotInstalled;
                Status = UiText.Get(_language, "not_installed");
                Details = UiText.Get(_language, "install_hint");
                RefreshVersionProperties();
                return;
            }
            State = LauncherState.ReadyToPlay;
            Status = UiText.Get(_language, "ready");
            Details = "";
            Details = GetLocalGameDetails();
            RefreshVersionProperties();
        }
        catch (Exception ex) { HandleError(UiText.Get(_language, "error_check_install"), ex); }
    }

    public async Task VerifyGameAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _retryOperation = RetryOperation.Verify;
        try
        {
            State = LauncherState.Verifying;
            Status = UiText.Get(_language, "checking_files");
            Details = UiText.Get(_language, "reading_manifest");
            CurrentFile = "";
            Progress = 0;
            ProgressText = "";
            SetProgressIndeterminate(true);
            _manifest = await _distribution.GetGameManifestAsync();
            SetAvailableGameVersion(_manifest.Version);
            var verifyProgress = new Progress<(string FileName, int Checked, int Total)>(value =>
            {
                CurrentFile = value.FileName;
                SetProgressIndeterminate(value.Total <= 0);
                Progress = value.Total == 0 ? 0 : value.Checked * 100d / value.Total;
                ProgressText = UiText.Get(_language, "verify_count", value.Checked, value.Total);
            });
            _invalidFiles = await _verifier.FindInvalidFilesAsync(_config.InstallDirectory, _manifest, verifyProgress);
            var cleanupCount = _installer.GetPendingCleanupCount(_config.InstallDirectory, _manifest);
            var pendingFiles = _invalidFiles.Count + cleanupCount;
            if (pendingFiles > 0)
            {
                if (!string.Equals(_manifest.Version, _config.InstalledGameVersion, StringComparison.OrdinalIgnoreCase))
                {
                    State = LauncherState.Updating;
                    Status = UiText.Get(_language, "update_available");
                    Details = UiText.Get(_language, "update_count", _manifest.Version, pendingFiles);
                }
                else
                {
                    State = LauncherState.Repairing;
                    Status = UiText.Get(_language, "repair_needed");
                    Details = UiText.Get(_language, "repair_count", pendingFiles);
                }
            }
            else
            {
                _config.InstalledGameVersion = _manifest.Version;
                ConfigurationService.Save(_config, _logger);
                State = LauncherState.ReadyToPlay;
                Status = UiText.Get(_language, "ready");
                Details = "";
                Details = UiText.Get(_language, "game_version", _manifest.Version);
                RefreshVersionProperties();
            }
        }
        catch (ReleaseNotPublishedException ex) { HandleReleaseNotPublished(ex); }
        catch (Exception ex) { HandleRemoteGameOperationError(UiText.Get(_language, "error_verify"), ex); }
        finally { SetProgressIndeterminate(false); IsBusy = false; }
    }

    public async Task InstallOrUpdateGameAsync(string? selectedDirectory = null)
    {
        if (IsBusy) return;
        if (State == LauncherState.NotInstalled)
        {
            if (string.IsNullOrWhiteSpace(selectedDirectory)) return;
            if (IsUnrealProjectOutputDirectory(selectedDirectory))
            {
                Status = UiText.Get(_language, "invalid_install_directory");
                Details = UiText.Get(_language, "install_package_root");
                return;
            }
            _config.InstallDirectory = selectedDirectory;
            ConfigurationService.Save(_config, _logger);
        }
        else if (IsUnrealProjectOutputDirectory(_config.InstallDirectory))
        {
            State = LauncherState.NotInstalled;
            Status = UiText.Get(_language, "invalid_install_directory");
            Details = UiText.Get(_language, "install_package_root");
            return;
        }
        IsBusy = true;
        _retryOperation = RetryOperation.Install;
        try
        {
            CurrentFile = "";
            SetProgressIndeterminate(true);
            _manifest = await _distribution.GetGameManifestAsync();
            SetAvailableGameVersion(_manifest.Version);
            var updating = File.Exists(Path.Combine(_config.InstallDirectory, _config.GameExecutableName))
                && !_manifest.Version.Equals(_config.InstalledGameVersion, StringComparison.OrdinalIgnoreCase);
            State = updating ? LauncherState.Updating : State == LauncherState.NotInstalled ? LauncherState.Downloading : LauncherState.Repairing;
            Status = UiText.Get(_language, updating ? "updating_game" : State == LauncherState.Downloading ? "installing_game" : "repairing_game");
            Details = UiText.Get(_language, "validating_download");
            Progress = 0;
            ProgressText = UiText.Get(_language, "calculating_download");
            var progress = new Progress<OperationProgress>(value =>
            {
                CurrentFile = value.FileName;
                SetProgressIndeterminate(value.TotalExpected <= 0);
                Progress = value.Percent;
                var received = FormatBytes(value.TotalReceived);
                var remaining = FormatBytes(Math.Max(0, value.TotalExpected - value.TotalReceived));
                var speed = FormatBytes((long)value.BytesPerSecond) + "/s";
                ProgressText = UiText.Get(_language, "download_progress", value.Percent, received, remaining, speed);
            });
            var count = await _installer.InstallOrRepairAsync(_config.InstallDirectory, _manifest, progress);
            var executable = Path.Combine(_config.InstallDirectory, _config.GameExecutableName);
            if (!File.Exists(executable)) throw new FileNotFoundException("The manifest does not include the configured game executable.", executable);
            _config.InstalledGameVersion = _manifest.Version;
            ConfigurationService.Save(_config, _logger);
            _logger.Info($"Game operation completed; changed {count} files");
            State = LauncherState.ReadyToPlay;
            Status = UiText.Get(_language, "ready");
            Details = "";
            Details = UiText.Get(_language, "game_version", _manifest.Version);
            Progress = 100;
            ProgressText = UiText.Get(_language, "install_verified");
            RefreshVersionProperties();
        }
        catch (ReleaseNotPublishedException ex) { HandleReleaseNotPublished(ex); }
        catch (Exception ex) { HandleRemoteGameOperationError(UiText.Get(_language, "error_install"), ex); }
        finally { SetProgressIndeterminate(false); IsBusy = false; }
    }

    public Task RetryLastOperationAsync() => _retryOperation == RetryOperation.Install
        ? InstallOrUpdateGameAsync()
        : VerifyGameAsync();

    public Process? LaunchGame()
    {
        if (State != LauncherState.ReadyToPlay || IsBusy) return null;
        try
        {
            var bootstrapExecutable = Path.GetFullPath(Path.Combine(_config.InstallDirectory, _config.GameExecutableName));
            var executable = GetShippingExecutablePath();
            if (!File.Exists(bootstrapExecutable) || !File.Exists(executable))
            {
                State = LauncherState.NotInstalled;
                Status = UiText.Get(_language, "not_installed");
                Details = UiText.Get(_language, "launch_missing");
                return null;
            }
            State = LauncherState.Launching;
            Status = UiText.Get(_language, "launching");
            _logger.Info($"Launching game from {executable}");
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = _config.InstallDirectory,
                UseShellExecute = false
            };
            if (!string.IsNullOrWhiteSpace(_config.UserName))
                startInfo.Environment["UBF_LAUNCHER_USERNAME"] = _config.UserName.Trim();
            var gameProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!gameProcess.Start())
            {
                gameProcess.Dispose();
                HandleError(UiText.Get(_language, "error_launch"), new InvalidOperationException("Process.Start returned false."));
                return null;
            }
            return gameProcess;
        }
        catch (Exception ex)
        {
            HandleError(UiText.Get(_language, "error_launch"), ex);
            return null;
        }
    }

    public void NotifyGameWindowUnavailable()
    {
        Status = _language == "en" ? "UBF did not open a window" : "UBF no mostró su ventana";
        Details = _language == "en"
            ? "The process is still running. Check the game log before trying again."
            : "El proceso sigue activo. Revisa el log del juego antes de volver a intentarlo.";
    }

    public void NotifyGameWindowStarted()
    {
        if (State != LauncherState.Launching) return;
        Status = UiText.Get(_language, "game_running");
        Details = "";
    }

    public void NotifyGameProcessExited()
    {
        if (State == LauncherState.Launching)
            State = HasLocalGameExecutable ? LauncherState.ReadyToPlay : LauncherState.NotInstalled;

        if (State == LauncherState.ReadyToPlay)
        {
            Status = UiText.Get(_language, "ready");
            Details = "";
        }
    }

    public void NotifyGameStartupFailed()
    {
        if (State == LauncherState.Launching)
            State = HasLocalGameExecutable ? LauncherState.ReadyToPlay : LauncherState.NotInstalled;
        Status = UiText.Get(_language, "game_start_failed");
        Details = UiText.Get(_language, "game_start_failed_details");
    }

    public async Task<string> CheckLauncherUpdateAsync(bool allowAutoInstall = false)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var remote = await _distribution.GetLauncherVersionAsync(timeout.Token);
            if (remote is null)
            {
                SetAvailableLauncherVersion("");
                return "launcher_no_metadata";
            }
            var current = GetCurrentLauncherVersion();
            LauncherUpdateAvailable = VersionUtility.IsNewer(remote.Version, current);
            SetAvailableLauncherVersion(LauncherUpdateAvailable ? remote.Version : "");
            if (LauncherUpdateAvailable) _logger.Info($"Launcher update available: {remote.Version}");
            OnPropertyChanged(nameof(PrimaryAction));
            if (!LauncherUpdateAvailable) return "launcher_current";
            if (allowAutoInstall && _config.AutomaticLauncherUpdates)
            {
                await UpdateLauncherAsync();
                return "launcher_downloading";
            }
            return "launcher_update_found";
        }
        catch (Exception ex) { _logger.Error("Launcher update check failed; continuing with current version", ex); return "error_launcher_check"; }
    }

    public async Task UpdateLauncherAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var updateHandoffStarted = false;
        var stateBeforeUpdate = State;
        var statusBeforeUpdate = Status;
        var detailsBeforeUpdate = Details;
        string? updateTemp = null;
        try
        {
            var remote = await _distribution.GetLauncherVersionAsync() ?? throw new InvalidDataException("No launcher release metadata is available.");
            if (!VersionUtility.IsNewer(remote.Version, GetCurrentLauncherVersion())) { LauncherUpdateAvailable = false; SetAvailableLauncherVersion(""); return; }
            var installedUpdater = Path.Combine(AppContext.BaseDirectory, "UBFLauncherUpdater.exe");
            if (!File.Exists(installedUpdater)) throw new FileNotFoundException("The launcher updater is not installed beside the launcher.", installedUpdater);
            updateTemp = Path.Combine(Path.GetTempPath(), "UBFLauncherUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updateTemp);
            var updater = Path.Combine(updateTemp, "UBFLauncherUpdater.exe");
            File.Copy(installedUpdater, updater);
            var archive = Path.Combine(updateTemp, "launcher-update.zip");
            Status = UiText.Get(_language, "launcher_downloading");
            Details = UiText.Get(_language, "launcher_version", remote.Version);
            CurrentFile = "";
            Progress = 0;
            ProgressText = UiText.Get(_language, "calculating_download");
            SetProgressIndeterminate(true);
            var downloadProgress = new Progress<(long Received, long Total)>(value =>
            {
                if (value.Total <= 0)
                {
                    SetProgressIndeterminate(true);
                    Progress = 0;
                    ProgressText = UiText.Get(_language, "download_progress", "—", FormatBytes(value.Received), "—", "—");
                    return;
                }

                SetProgressIndeterminate(false);
                Progress = value.Received * 100d / value.Total;
                ProgressText = UiText.Get(_language, "download_progress", Math.Round(Progress), FormatBytes(value.Received),
                    FormatBytes(Math.Max(0, value.Total - value.Received)), "—");
            });
            await _distribution.DownloadAsync(new Uri(remote.DownloadUrl), archive, downloadProgress);
            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!hash.Equals(remote.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Launcher update package failed SHA-256 validation.");
            }
            var args = $"--apply \"{archive}\" \"{AppContext.BaseDirectory}\" {Environment.ProcessId} \"{updateTemp}\"";
            Process.Start(new ProcessStartInfo(updater, args) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
            updateHandoffStarted = true;
            UpdateHandoffRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            if (updateTemp is not null)
            {
                try { if (Directory.Exists(updateTemp)) Directory.Delete(updateTemp, true); } catch { }
            }
            HandleLauncherUpdateFailure(stateBeforeUpdate, statusBeforeUpdate, detailsBeforeUpdate, ex);
        }
        finally
        {
            SetProgressIndeterminate(false);
            if (!updateHandoffStarted) IsBusy = false;
        }
    }

    public void ChangeUserName()
    {
        UserName = _config.UserName ?? "";
        State = LauncherState.EnteringUsername;
        Details = "";
    }

    public void SaveMusicVolume(double value) { _audio.SetVolume(value); OnPropertyChanged(nameof(MusicVolume)); }
    public void ToggleMusicMute() { _audio.ToggleMute(); OnPropertyChanged(nameof(MusicVolume)); }

    public void ApplyLanguage(string language)
    {
        _language = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
        var statusKey = _status switch
        {
            "Bienvenido" or "Welcome" => "welcome",
            "Comprobando UBF" or "Checking UBF" => "checking_game",
            "UBF no está instalado" or "UBF is not installed" => "not_installed",
            "Tu instalación necesita verificación" or "Your installation needs verification" => "verification_needed",
            "Comprobando archivos..." or "Checking files..." => "checking_files",
            "Hay una actualización disponible" or "An update is available" => "update_available",
            "Archivos dañados o faltantes" or "Damaged or missing files" => "repair_needed",
            "Listo para jugar" or "Ready to play" => "ready",
            "Actualizando UBF..." or "Updating UBF..." => "updating_game",
            "Instalando UBF..." or "Installing UBF..." => "installing_game",
            "Reparando instalación..." or "Repairing installation..." => "repairing_game",
            "Iniciando UBF..." or "Starting UBF..." => "launching",
            "UBF está en ejecución" or "UBF is running" => "game_running",
            "Descargando actualización del launcher..." or "Downloading launcher update..." => "launcher_downloading",
            _ => null
        };
        if (statusKey is not null) Status = UiText.Get(_language, statusKey);
        var detailsKey = _details switch
        {
            "Instala el juego para continuar." or "Install the game to continue." => "install_hint",
            "Comprobaremos los archivos antes de iniciar el juego." or "Game files need to be checked before playing." => "verify_before_play",
            "Leyendo la versión disponible y verificando la instalación." or "Checking the available version and verifying the installation." => "reading_manifest",
            "Los archivos se validan antes de aplicarse." or "Files are validated before being installed." => "validating_download",
            "Selecciona una carpeta para instalarlo." or "Choose a folder to install the game." => "launch_missing",
            "Puedes reintentar la comprobación." or "You can retry the check." => "retry_check",
            _ => null
        };
        if (detailsKey is not null) Details = UiText.Get(_language, detailsKey);
        RefreshVersionProperties();
        OnPropertyChanged(nameof(PrimaryAction));
    }

    private void HandleError(string message, Exception ex)
    {
        _logger.Error(message, ex);
        State = LauncherState.Error;
        Status = message;
        Details = ex is InvalidDataException ? ex.Message : UiText.Get(_language, "retry_check");
        ProgressText = "";
    }

    private void HandleReleaseNotPublished(ReleaseNotPublishedException ex)
    {
        _logger.Info(ex.Message);
        State = HasLocalGameExecutable ? LauncherState.ReadyToPlay : LauncherState.NotInstalled;
        Status = UiText.Get(_language, "game_release_not_published");
        var owner = _config.GameRepository.Split('/').FirstOrDefault() ?? "RPmods";
        var repository = _config.GameRepository.Split('/').Skip(1).FirstOrDefault() ?? "ubf";
        Details = UiText.Get(_language, "game_release_not_published_details", _config.TargetGameVersion, owner, repository);
        if (HasLocalGameExecutable)
            Details += " " + UiText.Get(_language, "local_play_available");
    }

    private bool HasLocalGameExecutable =>
        File.Exists(Path.Combine(_config.InstallDirectory, _config.GameExecutableName)) &&
        File.Exists(GetShippingExecutablePath());

    private static bool IsUnrealProjectOutputDirectory(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            var projectDirectory = directory.Parent?.Parent;
            return directory.Name.Equals("Win64", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase) == true &&
                projectDirectory is not null &&
                File.Exists(Path.Combine(projectDirectory.FullName, "UBF.uproject"));
        }
        catch { return false; }
    }

    private string GetShippingExecutablePath() => Path.GetFullPath(Path.Combine(
        _config.InstallDirectory, "UBF", "Binaries", "Win64", "UBF-Win64-Shipping.exe"));

    private string GetLocalGameDetails() => string.IsNullOrWhiteSpace(_config.InstalledGameVersion)
        ? UiText.Get(_language, "local_game_unversioned")
        : UiText.Get(_language, "local_game_version", _config.InstalledGameVersion);

    private void HandleRemoteGameOperationError(string message, Exception ex)
    {
        HandleError(message, ex);
        if (HasLocalGameExecutable)
            Details += " " + UiText.Get(_language, "previous_installation_kept");
        RefreshVersionProperties();
    }

    private void HandleLauncherUpdateFailure(LauncherState stateBeforeUpdate, string statusBeforeUpdate, string detailsBeforeUpdate, Exception ex)
    {
        var restoredState = stateBeforeUpdate is LauncherState.ReadyToPlay or LauncherState.NotInstalled or LauncherState.Updating or LauncherState.Repairing
            ? stateBeforeUpdate
            : HasLocalGameExecutable ? LauncherState.ReadyToPlay : LauncherState.NotInstalled;
        _logger.Error(UiText.Get(_language, "error_launcher_update"), ex);
        State = restoredState;
        Status = statusBeforeUpdate;
        var error = UiText.Get(_language, "error_launcher_update");
        Details = string.IsNullOrWhiteSpace(detailsBeforeUpdate) ? error : $"{detailsBeforeUpdate} {error}";
        RefreshVersionProperties();
    }

    private void SetAvailableGameVersion(string version)
    {
        _availableGameVersion = version;
        OnPropertyChanged(nameof(AvailableGameVersion));
        OnPropertyChanged(nameof(HasGameUpdate));
    }

    private void SetAvailableLauncherVersion(string version)
    {
        _availableLauncherVersion = version;
        OnPropertyChanged(nameof(AvailableLauncherVersion));
        OnPropertyChanged(nameof(HasLauncherUpdate));
    }

    private void RefreshVersionProperties()
    {
        OnPropertyChanged(nameof(InstalledGameVersion));
        OnPropertyChanged(nameof(AvailableGameVersion));
        OnPropertyChanged(nameof(HasGameUpdate));
        OnPropertyChanged(nameof(LauncherInstalledVersion));
        OnPropertyChanged(nameof(AvailableLauncherVersion));
        OnPropertyChanged(nameof(HasLauncherUpdate));
    }

    private void SetProgressIndeterminate(bool value)
    {
        if (_progressIsIndeterminate == value) return;
        _progressIsIndeterminate = value;
        OnPropertyChanged(nameof(IsProgressIndeterminate));
    }

    private static string GetCurrentLauncherVersion() => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.14";

    private enum RetryOperation { Verify, Install }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = Math.Max(0, value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
