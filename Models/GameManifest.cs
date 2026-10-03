namespace UBFLauncher.Models;

public sealed class GameManifest
{
    public string Version { get; set; } = "1.0.1-beta";
    public GamePackage? Package { get; set; }
    public List<GameFile> Files { get; set; } = [];
}

public sealed class GamePackage
{
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class GameFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string? DownloadUrl { get; set; }
}
