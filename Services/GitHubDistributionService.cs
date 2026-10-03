using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using UBFLauncher.Models;

namespace UBFLauncher.Services;

public sealed class GitHubDistributionService : IDistributionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly LauncherConfig _config;
    private readonly Logger _logger;
    private readonly Dictionary<string, string> _branches = new(StringComparer.OrdinalIgnoreCase);

    public GitHubDistributionService(LauncherConfig config, Logger logger)
    {
        _config = config;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UBFLauncher", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<GameManifest> GetGameManifestAsync(CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(_config.GameManifestUrlOverride)
            ? await RawFileUrlAsync(_config.GameRepository, _config.GameManifestPath, cancellationToken)
            : RequireHttps(_config.GameManifestUrlOverride);
        _logger.Info($"Fetching game manifest from {url}");
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new ReleaseNotPublishedException($"Game manifest is not published at {url} yet.");
        response.EnsureSuccessStatusCode();
        var manifest = await response.Content.ReadFromJsonAsync<GameManifest>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The game manifest is empty.");
        ValidateManifest(manifest);
        if (!manifest.Version.Equals(_config.TargetGameVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"This launcher is pinned to game version {_config.TargetGameVersion}; the repository manifest declares {manifest.Version}.");
        if (!manifest.Files.Any(file => file.Path.Equals(_config.GameExecutableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The game manifest must include {_config.GameExecutableName}.");
        return manifest;
    }

    public async Task<LauncherVersion?> GetLauncherVersionAsync(CancellationToken cancellationToken = default)
    {
        Uri url;
        try
        {
            url = string.IsNullOrWhiteSpace(_config.LauncherVersionUrlOverride)
                ? await RawFileUrlAsync(_config.LauncherRepository, _config.LauncherVersionPath, cancellationToken)
                : RequireHttps(_config.LauncherVersionUrlOverride);
        }
        catch (ReleaseNotPublishedException ex)
        {
            _logger.Info(ex.Message);
            return null;
        }
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var version = await response.Content.ReadFromJsonAsync<LauncherVersion>(JsonOptions, cancellationToken);
        if (version is not null && !VersionUtility.IsValid(version.Version))
            throw new InvalidDataException("Launcher version metadata contains an invalid semantic version.");
        if (version is not null && (!Uri.TryCreate(version.DownloadUrl, UriKind.Absolute, out var download) || download.Scheme != Uri.UriSchemeHttps || !IsSha256(version.Sha256)))
            throw new InvalidDataException("Launcher version metadata must provide an HTTPS downloadUrl and a SHA-256 hash.");
        return version;
    }

    public Task DownloadGamePackageAsync(GamePackage package, string destination, IProgress<(long Received, long Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.FileName) ||
            !package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            package.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            package.FileName.Contains('/') || package.FileName.Contains('\\') ||
            package.FileName is "." or "..")
            throw new InvalidDataException("The game package must have a safe ZIP file name.");

        var uri = RequireGameReleaseAsset(package.DownloadUrl, _config.GameRepository, package.FileName);
        return DownloadAsync(uri, destination, progress, cancellationToken);
    }

    public async Task DownloadAsync(Uri uri, string destination, IProgress<(long Received, long Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Downloads must use HTTPS.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report((received, total));
            }
            await output.FlushAsync(cancellationToken);
            File.Move(temp, destination, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private async Task<Uri> RawFileUrlAsync(string repository, string path, CancellationToken cancellationToken)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"Invalid GitHub repository: {repository}");
        if (!_branches.TryGetValue(repository, out var branch))
        {
            var infoUrl = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}");
            using var response = await _http.GetAsync(infoUrl, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
                throw new ReleaseNotPublishedException($"GitHub repository https://github.com/{parts[0]}/{parts[1]} has no published branch or release metadata yet.");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            branch = document.RootElement.TryGetProperty("default_branch", out var branchValue)
                ? branchValue.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(branch))
                throw new ReleaseNotPublishedException($"GitHub repository https://github.com/{parts[0]}/{parts[1]} does not have a published branch yet.");
            _branches[repository] = branch;
        }
        var encodedPath = string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return new Uri($"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{Uri.EscapeDataString(branch)}/{encodedPath}");
    }

    private static void ValidateManifest(GameManifest manifest)
    {
        if (!VersionUtility.IsValid(manifest.Version))
            throw new InvalidDataException("Manifest version must use major.minor.patch format, optionally with a prerelease suffix such as 1.0.1-beta.");
        if (manifest.Files is null || manifest.Files.Count == 0) throw new InvalidDataException("Manifest has no files.");
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!IsSafeRelativePath(file.Path) || !uniquePaths.Add(file.Path.Replace('\\', '/')))
                throw new InvalidDataException($"Manifest contains an invalid or duplicate path: {file.Path}");
            if (file.Size < 0 || !IsSha256(file.Sha256)) throw new InvalidDataException($"Manifest entry is missing a valid size or SHA-256: {file.Path}");
            if (!string.IsNullOrWhiteSpace(file.DownloadUrl)) _ = RequireHttps(file.DownloadUrl);
        }
    }

    public static Uri RequireGameReleaseAsset(string value, string repository, string fileName)
    {
        var uri = RequireHttps(value);
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Invalid GitHub repository: {repository}");

        var releasePrefix = $"/{parts[0]}/{parts[1]}/releases/download/";
        var assetName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase) ||
            !assetName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The game ZIP must use the direct HTTPS asset URL from this repository's GitHub Release.");

        return uri;
    }

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Any(char.IsControl)) return false;
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)) return false;

        foreach (var part in normalized.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' '))
                return false;

            var deviceName = part.Split('.')[0];
            if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                IsNumberedDeviceName(deviceName, "COM") || IsNumberedDeviceName(deviceName, "LPT"))
                return false;
        }

        return true;
    }

    private static bool IsNumberedDeviceName(string value, string prefix) =>
        value.Length == prefix.Length + 1 && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        value[^1] is >= '1' and <= '9';

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static Uri RequireHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri : throw new InvalidDataException("Configured URLs must be absolute HTTPS URLs.");
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
