using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using UBFLauncher.Models;
using UBFLauncher.Services;

namespace UBFLauncher;

public partial class IntroWindow : Window
{
    private readonly string _logoPath;
    private readonly Logger _logger;
    private readonly Action _finished;
    private readonly AudioService _audio;
    private readonly DispatcherTimer _fallbackTimer;
    private int _completed;

    public IntroWindow(string logoPath, AudioService audio, Logger logger, Action finished)
    {
        InitializeComponent();
        _logoPath = logoPath;
        _audio = audio;
        _logger = logger;
        _finished = finished;
        _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _fallbackTimer.Tick += (_, _) =>
        {
            _logger.Info("Logo intro timed out; opening the launcher");
            FinishIntro();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Info($"Showing logo intro: {_logoPath}");
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(_logoPath);
            image.EndInit();
            image.Freeze();
            IntroLogo.Source = image;
            _audio.Start();
            _fallbackTimer.Start();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
            IntroLogo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            await Task.Delay(TimeSpan.FromSeconds(3));
            if (Volatile.Read(ref _completed) != 0) return;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(3))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => FinishIntro();
            IntroLogo.BeginAnimation(OpacityProperty, fadeOut);
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(3))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            await Task.Delay(TimeSpan.FromSeconds(3));
            FinishIntro();
        }
        catch (Exception ex)
        {
            _logger.Error("Logo intro could not be displayed", ex);
            FinishIntro();
        }
    }

    private void FinishIntro()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        if (Dispatcher.CheckAccess()) CompleteTransition();
        else Dispatcher.BeginInvoke(CompleteTransition);
    }

    private void CompleteTransition()
    {
        _fallbackTimer.Stop();
        try { _finished(); }
        catch (Exception ex)
        {
            _logger.Error("Could not open the launcher after the logo intro", ex);
            MessageBox.Show("No se pudo abrir el launcher. Revisa Logs/launcher.log e inténtalo nuevamente.",
                "UBF Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (IsVisible) Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _fallbackTimer.Stop();
        base.OnClosed(e);
    }
}
