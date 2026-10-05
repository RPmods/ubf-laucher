using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UBFLauncher.Models;
using UBFLauncher.Services;

namespace UBFLauncher;

public partial class App : Application
{
    private readonly Logger _logger = new();
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private const int SwRestore = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var executablePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        var installationKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(executablePath).ToUpperInvariant())));
        _instanceMutex = new Mutex(true, $@"Local\RPmods.UBFLauncher.{installationKey}", out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show("Ocurrió un error inesperado. Revisa Logs/launcher.log e inténtalo nuevamente.",
                "UBF Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger.Error("Unhandled application exception", args.ExceptionObject as Exception);

        var configuration = ConfigurationService.Load(_logger);
        var distribution = new GitHubDistributionService(configuration, _logger);
        var verifier = new GameVerifier(_logger, configuration.FileVerification);
        var gameInstaller = new GameInstaller(distribution, verifier, _logger);
        var audio = new AudioService(configuration, _logger);
        var viewModel = new MainViewModel(configuration, distribution, verifier, gameInstaller, audio, _logger);
        var introPath = ResolveAsset(configuration.LogoPath);
        IntroWindow? intro = null;

        void ShowLauncher()
        {
            var window = new MainWindow(viewModel, audio, configuration, _logger);
            MainWindow = window;
            window.Show();
            if (intro is not null)
            {
                var completedIntro = intro;
                intro = null;
                completedIntro.Close();
            }
        }

        if (File.Exists(introPath))
        {
            try
            {
                intro = new IntroWindow(introPath, audio, _logger, ShowLauncher);
                MainWindow = intro;
                intro.Show();
            }
            catch (Exception ex)
            {
                _logger.Error("Could not start the logo intro; opening the launcher directly", ex);
                ShowLauncher();
            }
        }
        else
        {
            _logger.Info($"Intro logo not found; opening launcher directly: {introPath}");
            ShowLauncher();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstanceMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ActivateExistingInstance()
    {
        var currentProcessId = (uint)Environment.ProcessId;
        using var currentProcess = Process.GetCurrentProcess();
        var processName = currentProcess.ProcessName;
        var existingProcessIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if ((uint)process.Id != currentProcessId) existingProcessIds.Add((uint)process.Id);
            }
            finally { process.Dispose(); }
        }

        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var ownerProcessId);
            if (!existingProcessIds.Contains(ownerProcessId)) return true;
            ShowWindow(window, SwRestore);
            SetForegroundWindow(window);
            return false;
        }, IntPtr.Zero);
    }

    private static string ResolveAsset(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
