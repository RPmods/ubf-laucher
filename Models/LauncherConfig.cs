using System.Text.Json.Serialization;

namespace UBFLauncher.Models;

public sealed class LauncherConfig
{
    public string GameRepository { get; set; } = "RPmods/ubf";
    public string LauncherRepository { get; set; } = "RPmods/ubf-laucher";
    public string GameManifestPath { get; set; } = "manifest.json";
    public string LauncherVersionPath { get; set; } = "version.json";
    public string TargetGameVersion { get; set; } = "1.0.1-beta";
    public string GameExecutableName { get; set; } = "UBF.EXE";
    public string InstallDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UBF", "Game");
    public string BackgroundVideoPath { get; set; } = "Assets/background.mp4";
    public string MusicPath { get; set; } = "Assets/menu_sd_laucher_badays.mp3";
    public string AlternateMusicPath { get; set; } = "Assets/menu_sd_laucher_goodays.mp3";
    public string LogoPath { get; set; } = "Assets/logo.png";
    public string Language { get; set; } = "es";
    public bool AutomaticLauncherUpdates { get; set; }
    public FileVerificationOptions FileVerification { get; set; } = new();
    public double MusicVolume { get; set; } = 0.65;
    public string? UserName { get; set; }
    public string? InstalledGameVersion { get; set; }
    public Dictionary<string, string> SocialLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Discord"] = "", ["YouTube"] = "", ["Twitch"] = "https://www.twitch.tv/rodrigorpmods", ["TikTok"] = "", ["Web"] = ""
    };

    [JsonIgnore]
    public string GameManifestUrlOverride { get; set; } = "";
    [JsonIgnore]
    public string LauncherVersionUrlOverride { get; set; } = "";
}

public sealed class FileVerificationOptions
{
    public bool VerifySha256 { get; set; } = true;
    public int HashBufferSizeKb { get; set; } = 128;
}

public sealed class LauncherSettingsFile
{
    public LauncherConfig Settings { get; set; } = new();
    public string? GameManifestUrl { get; set; }
    public string? LauncherVersionUrl { get; set; }
}
