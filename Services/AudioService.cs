using System.Windows.Media;
using System.Windows.Threading;
using UBFLauncher.Models;

namespace UBFLauncher.Services;

public sealed class AudioService : IDisposable
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FallbackTrackEnd = TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(31);
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly string[] _configuredTracks;
    private readonly LauncherConfig _config;
    private readonly Logger _logger;
    private string[] _tracks = [];
    private double _volume;
    private TimeSpan _fadeStart;
    private TimeSpan _trackEnd = FallbackTrackEnd;
    private int _trackIndex = -1;
    private int _failedOpenAttempts;
    private bool _opened;
    private bool _started;

    public AudioService(LauncherConfig config, Logger logger)
    {
        _config = config;
        _logger = logger;
        _volume = Math.Clamp(config.MusicVolume, 0, 1);
        _configuredTracks = new[] { config.MusicPath, config.AlternateMusicPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ResolveAsset)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _timer.Tick += OnTick;
        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            _failedOpenAttempts = 0;
            _player.Position = TimeSpan.Zero;
            _trackEnd = _player.NaturalDuration.HasTimeSpan && _player.NaturalDuration.TimeSpan > TimeSpan.Zero
                ? _player.NaturalDuration.TimeSpan
                : FallbackTrackEnd;
            _fadeStart = _trackEnd > FadeDuration ? _trackEnd - FadeDuration : TimeSpan.Zero;
            ApplyCurrentVolume();
            _player.Play();
            _timer.Start();
            _logger.Info($"Playing menu music: {Path.GetFileName(_tracks[_trackIndex])}");
        };
        _player.MediaEnded += (_, _) => { if (_opened) OpenNextTrack(); };
        _player.MediaFailed += (_, args) =>
        {
            _logger.Error("Background music could not be played: " + args.ErrorException?.Message, args.ErrorException);
            _opened = false;
            _timer.Stop();
            TryNextAfterFailure();
        };
    }

    public double Volume => _volume;
    public bool IsMuted { get; private set; }
    public event EventHandler? VolumeChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _tracks = _configuredTracks.Where(File.Exists).ToArray();
        foreach (var track in _configuredTracks.Except(_tracks, StringComparer.OrdinalIgnoreCase))
            _logger.Info($"Music asset not found: {track}");
        if (_tracks.Length == 0) return;
        OpenNextTrack();
    }

    public void SetVolume(double value)
    {
        _volume = Math.Clamp(value, 0, 1);
        IsMuted = _volume <= 0;
        ApplyCurrentVolume();
        _config.MusicVolume = _volume;
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMute()
    {
        if (IsMuted) { IsMuted = false; if (_volume <= 0) _volume = 0.65; }
        else IsMuted = true;
        ApplyCurrentVolume();
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!_opened) return;
        var position = _player.Position;
        if (position >= _trackEnd) { OpenNextTrack(); return; }
        var fade = position <= _fadeStart ? 1d : Math.Clamp((_trackEnd - position).TotalMilliseconds / (_trackEnd - _fadeStart).TotalMilliseconds, 0, 1);
        _player.Volume = IsMuted ? 0 : _volume * fade;
    }

    private void OpenNextTrack()
    {
        if (_tracks.Length == 0) return;
        _timer.Stop();
        _opened = false;
        _trackIndex = (_trackIndex + 1) % _tracks.Length;
        try { _player.Open(new Uri(_tracks[_trackIndex])); }
        catch (Exception ex)
        {
            _logger.Error("Could not open background music", ex);
            TryNextAfterFailure();
        }
    }

    private void TryNextAfterFailure()
    {
        if (_tracks.Length < 2 || ++_failedOpenAttempts >= _tracks.Length) return;
        OpenNextTrack();
    }

    private void ApplyCurrentVolume() => _player.Volume = IsMuted ? 0 : _volume;
    private static string ResolveAsset(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    public void Dispose()
    {
        _timer.Stop();
        _player.Close();
    }
}
