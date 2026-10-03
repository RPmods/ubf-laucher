using System.Text.Json;
using UBFLauncher.Models;

namespace UBFLauncher.Services;

public static class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UBFLauncher", "config.json");

    public static LauncherConfig Load(Logger logger)
    {
        try
        {
            LauncherConfig config;
            if (File.Exists(ConfigPath))
            {
                var file = JsonSerializer.Deserialize<LauncherSettingsFile>(File.ReadAllText(ConfigPath), JsonOptions);
                config = file?.Settings ?? new LauncherConfig();
                config.GameManifestUrlOverride = file?.GameManifestUrl ?? "";
                config.LauncherVersionUrlOverride = file?.LauncherVersionUrl ?? "";
            }
            else config = new LauncherConfig();

            var template = Path.Combine(AppContext.BaseDirectory, "launcher.settings.json");
            if (File.Exists(template))
            {
                var overlay = JsonSerializer.Deserialize<LauncherSettingsFile>(File.ReadAllText(template), JsonOptions);
                if (overlay is not null)
                {
                    config.GameRepository = overlay.Settings.GameRepository;
                    config.LauncherRepository = overlay.Settings.LauncherRepository;
                    config.GameManifestPath = overlay.Settings.GameManifestPath;
                    config.LauncherVersionPath = overlay.Settings.LauncherVersionPath;
                    config.TargetGameVersion = overlay.Settings.TargetGameVersion;
                    config.BackgroundVideoPath = overlay.Settings.BackgroundVideoPath;
                    config.MusicPath = overlay.Settings.MusicPath;
                    config.AlternateMusicPath = overlay.Settings.AlternateMusicPath;
                    config.LogoPath = overlay.Settings.LogoPath;
                    config.FileVerification = overlay.Settings.FileVerification;
                    if (!string.IsNullOrWhiteSpace(overlay.GameManifestUrl)) config.GameManifestUrlOverride = overlay.GameManifestUrl;
                    if (!string.IsNullOrWhiteSpace(overlay.LauncherVersionUrl)) config.LauncherVersionUrlOverride = overlay.LauncherVersionUrl;
                    foreach (var entry in overlay.Settings.SocialLinks) config.SocialLinks[entry.Key] = entry.Value;
                }
            }
            return config;
        }
        catch (Exception ex)
        {
            logger.Error("Could not read configuration; defaults will be used", ex);
            return new LauncherConfig();
        }
    }

    public static void Save(LauncherConfig config, Logger logger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var value = new LauncherSettingsFile
            {
                Settings = config,
                GameManifestUrl = config.GameManifestUrlOverride,
                LauncherVersionUrl = config.LauncherVersionUrlOverride
            };
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, ConfigPath, true);
        }
        catch (Exception ex) { logger.Error("Could not save configuration", ex); }
    }
}
