namespace UBFLauncher.Models;

public sealed class LauncherVersion
{
    public string Version { get; set; } = "0.0.0";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string? ReleaseNotes { get; set; }
}
