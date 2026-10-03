using UBFLauncher.Models;

namespace UBFLauncher.Services;

public interface IDistributionService
{
    Task<GameManifest> GetGameManifestAsync(CancellationToken cancellationToken = default);
    Task<LauncherVersion?> GetLauncherVersionAsync(CancellationToken cancellationToken = default);
    Task DownloadGamePackageAsync(GamePackage package, string destination, IProgress<(long Received, long Total)>? progress,
        CancellationToken cancellationToken = default);
    Task DownloadAsync(Uri uri, string destination, IProgress<(long Received, long Total)>? progress,
        CancellationToken cancellationToken = default);
}
