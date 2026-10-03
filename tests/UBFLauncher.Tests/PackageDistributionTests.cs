using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using UBFLauncher.Models;
using UBFLauncher.Services;
using Xunit;

namespace UBFLauncher.Tests;

public sealed class PackageDistributionTests
{
    [Fact]
    public async Task CleanInstallDownloadsZipValidatesFilesAndPreservesUserData()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        var savePath = Path.Combine(root, "Saved", "SaveGames", "slot.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllTextAsync(savePath, "user save");

        var gameFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBF.exe"] = [1, 2, 3, 4],
            ["UBF/Content/Paks/UBF-Windows.pak"] = [5, 6, 7, 8, 9]
        };
        var archiveBytes = CreateZip(gameFiles);
        var installer = CreateInstaller(temp.Path, archiveBytes);
        var manifest = CreateManifest(archiveBytes, gameFiles);

        var changed = await installer.InstallOrRepairAsync(root, manifest, null);

        Assert.Equal(2, changed);
        foreach (var pair in gameFiles)
            Assert.Equal(pair.Value, await File.ReadAllBytesAsync(GameVerifier.ResolvePath(root, pair.Key)));
        Assert.Equal("user save", await File.ReadAllTextAsync(savePath));
        Assert.Empty(await new GameVerifier(new Logger(Path.Combine(temp.Path, "verify.log")))
            .FindInvalidFilesAsync(root, manifest));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Fact]
    public async Task UpdateRepairsOnlyManifestFilesAndKeepsExtraFiles()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(Path.Combine(root, "Saved"));
        await File.WriteAllTextAsync(Path.Combine(root, "UBF.exe"), "old executable");
        await File.WriteAllTextAsync(Path.Combine(root, "Saved", "keep.ini"), "player data");

        var gameFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBF.exe"] = [10, 11, 12],
            ["UBF/Content/Paks/UBF-Windows.pak"] = [13, 14, 15]
        };
        var archiveBytes = CreateZip(gameFiles);
        var installer = CreateInstaller(temp.Path, archiveBytes);

        var changed = await installer.InstallOrRepairAsync(root, CreateManifest(archiveBytes, gameFiles), null);

        Assert.Equal(2, changed);
        Assert.Equal(gameFiles["UBF.exe"], await File.ReadAllBytesAsync(Path.Combine(root, "UBF.exe")));
        Assert.Equal("player data", await File.ReadAllTextAsync(Path.Combine(root, "Saved", "keep.ini")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Fact]
    public async Task VerifyFindsCorruptFileWithoutDownloadingPackage()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "UBF.exe"), "corrupt");
        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = [1, 2, 3] };
        var archiveBytes = CreateZip(expected);
        var manifest = CreateManifest(archiveBytes, expected);
        var invalid = await new GameVerifier(new Logger(Path.Combine(temp.Path, "verify.log")))
            .FindInvalidFilesAsync(root, manifest);

        Assert.Single(invalid);
        Assert.Equal("UBF.exe", invalid[0].Path);
    }

    [Fact]
    public async Task AlreadyMatchingVersionDoesNotNeedOrDownloadPackage()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(root);
        var executable = new byte[] { 31, 32, 33 };
        await File.WriteAllBytesAsync(Path.Combine(root, "UBF.exe"), executable);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = executable };
        var manifest = CreateManifest(CreateZip(files), files);
        manifest.Package = null;
        var logger = new Logger(Path.Combine(temp.Path, "matching.log"));
        var distribution = new FakeDistributionService();
        var installer = new GameInstaller(distribution, new GameVerifier(logger), logger);

        Assert.Equal(0, await installer.InstallOrRepairAsync(root, manifest, null));
        Assert.Equal(0, distribution.PackageRequests);
    }

    [Fact]
    public async Task CorruptZipHashFailsBeforeChangingInstalledFiles()
    {
        using var temp = new TemporaryDirectory();
        var root = CreateExistingGame(temp.Path, "known good executable");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = [2, 4, 6] };
        var archiveBytes = CreateZip(files);
        var manifest = CreateManifest(archiveBytes, files);
        manifest.Package!.Sha256 = new string('0', 64);

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateInstaller(temp.Path, archiveBytes)
            .InstallOrRepairAsync(root, manifest, null));

        Assert.Equal("known good executable", await File.ReadAllTextAsync(Path.Combine(root, "UBF.exe")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Fact]
    public async Task PathTraversalZipIsRejectedBeforeCommit()
    {
        using var temp = new TemporaryDirectory();
        var root = CreateExistingGame(temp.Path, "known good executable");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = [3, 5, 7] };
        var archiveBytes = CreateZip(files, ("../outside.marker", [99]));

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateInstaller(temp.Path, archiveBytes)
            .InstallOrRepairAsync(root, CreateManifest(archiveBytes, files), null));

        Assert.Equal("known good executable", await File.ReadAllTextAsync(Path.Combine(root, "UBF.exe")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.marker")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Fact]
    public async Task UnlistedZipFileIsRejectedAndPreviousVersionRemains()
    {
        using var temp = new TemporaryDirectory();
        var root = CreateExistingGame(temp.Path, "known good executable");
        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = [8, 9, 10] };
        var archiveBytes = CreateZip(expected, ("payload.exe", [11, 12]));

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateInstaller(temp.Path, archiveBytes)
            .InstallOrRepairAsync(root, CreateManifest(archiveBytes, expected), null));

        Assert.Equal("known good executable", await File.ReadAllTextAsync(Path.Combine(root, "UBF.exe")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Fact]
    public async Task FailedCommitRollsBackFilesAlreadyReplaced()
    {
        using var temp = new TemporaryDirectory();
        var root = CreateExistingGame(temp.Path, "old executable");
        await File.WriteAllTextAsync(Path.Combine(root, "UBF"), "blocks directory creation");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBF.exe"] = [21, 22, 23],
            ["UBF/Content/Paks/UBF-Windows.pak"] = [24, 25, 26]
        };
        var archiveBytes = CreateZip(files);
        var manifest = CreateManifest(archiveBytes, files);

        await Assert.ThrowsAnyAsync<IOException>(() => CreateInstaller(temp.Path, archiveBytes)
            .InstallOrRepairAsync(root, manifest, null));

        Assert.Equal("old executable", await File.ReadAllTextAsync(Path.Combine(root, "UBF.exe")));
        Assert.Equal("blocks directory creation", await File.ReadAllTextAsync(Path.Combine(root, "UBF")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".ubf-staging-*"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/Windows/file.txt")]
    [InlineData("//server/share/file.txt")]
    [InlineData("Content/NUL.txt")]
    [InlineData("Content/file. ")]
    public void UnsafePathsAreRejected(string path) => Assert.False(GitHubDistributionService.IsSafeRelativePath(path));

    [Fact]
    public void PackageMustBeTheDirectAssetFromTheConfiguredGameRelease()
    {
        const string valid = "https://github.com/RPmods/ubf/releases/download/v1.0.1-beta/UBF-v1.0.1-beta.zip";
        Assert.Equal(valid, GitHubDistributionService.RequireGameReleaseAsset(valid, "RPmods/ubf", "UBF-v1.0.1-beta.zip").AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => GitHubDistributionService.RequireGameReleaseAsset(
            "https://raw.githubusercontent.com/RPmods/ubf/main/UBF-v1.0.1-beta.zip", "RPmods/ubf", "UBF-v1.0.1-beta.zip"));
        Assert.Throws<InvalidDataException>(() => GitHubDistributionService.RequireGameReleaseAsset(
            "https://github.com/other/game/releases/download/v1/UBF-v1.0.1-beta.zip", "RPmods/ubf", "UBF-v1.0.1-beta.zip"));
    }

    [Fact]
    public async Task TwitchVectorAssetLoadsFromLauncherResources()
    {
        var loaded = await RunOnStaAsync(() =>
        {
            _ = new Application();
            var resources = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UBFLauncher;component/Assets/TwitchMark.xaml", UriKind.Absolute)
            };
            return resources["TwitchMark"] is System.Windows.Media.ImageSource;
        });

        Assert.True(loaded);
    }

    [Fact]
    public async Task LocalPlayRemainsAvailableWhenRemoteManifestIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "UBF.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), executable);
        var remote = new FakeDistributionService(new ReleaseNotPublishedException("manifest 404"));
        var logger = new Logger(Path.Combine(temp.Path, "launcher.log"));

        await RunOnStaAsync(() =>
        {
            var config = new LauncherConfig { InstallDirectory = root, UserName = "LocalPlayer" };
            using var audio = new AudioService(config, logger);
            var verifier = new GameVerifier(logger);
            var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

            viewModel.CheckGamePresenceAsync().GetAwaiter().GetResult();
            Assert.Equal(LauncherState.ReadyToPlay, viewModel.State);
            Assert.Equal("JUGAR", viewModel.PrimaryAction);
            Assert.Equal(0, remote.ManifestRequests);

            viewModel.VerifyGameAsync().GetAwaiter().GetResult();
            Assert.Equal(1, remote.ManifestRequests);
            Assert.Equal(LauncherState.ReadyToPlay, viewModel.State);
            Assert.Equal("JUGAR", viewModel.PrimaryAction);

            using var game = viewModel.LaunchGame();
            Assert.NotNull(game);
            Assert.True(game!.WaitForExit(10_000));
            viewModel.NotifyGameProcessExited();
            Assert.Equal(LauncherState.ReadyToPlay, viewModel.State);
            return true;
        });
    }

    [Fact]
    public async Task MissingExecutableShowsInstallAndMissingReleaseKeepsInstallAction()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        var remote = new FakeDistributionService(new ReleaseNotPublishedException("manifest 404"));
        var logger = new Logger(Path.Combine(temp.Path, "launcher.log"));

        await RunOnStaAsync(() =>
        {
            var config = new LauncherConfig { InstallDirectory = root };
            using var audio = new AudioService(config, logger);
            var verifier = new GameVerifier(logger);
            var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

            viewModel.CheckGamePresenceAsync().GetAwaiter().GetResult();
            Assert.Equal(LauncherState.NotInstalled, viewModel.State);
            Assert.Equal("INSTALAR", viewModel.PrimaryAction);
            viewModel.VerifyGameAsync().GetAwaiter().GetResult();
            Assert.Equal(1, remote.ManifestRequests);
            Assert.Equal(LauncherState.NotInstalled, viewModel.State);
            Assert.Equal("INSTALAR", viewModel.PrimaryAction);
            Assert.False(File.Exists(Path.Combine(root, "UBF.exe")));
            return true;
        });
    }

    private static GameInstaller CreateInstaller(string temp, byte[] packageBytes)
    {
        var logger = new Logger(Path.Combine(temp, "installer.log"));
        var verifier = new GameVerifier(logger);
        return new GameInstaller(new FakeDistributionService(packageBytes), verifier, logger);
    }

    private static string CreateExistingGame(string temp, string executableContents)
    {
        var root = Path.Combine(temp, "game");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "UBF.exe"), executableContents);
        return root;
    }

    private static GameManifest CreateManifest(byte[] archiveBytes, IReadOnlyDictionary<string, byte[]> files) => new()
    {
        Version = "1.0.1-beta",
        Package = new GamePackage
        {
            FileName = "UBF-v1.0.1-beta.zip",
            DownloadUrl = "https://github.com/RPmods/ubf/releases/download/v1.0.1-beta/UBF-v1.0.1-beta.zip",
            Size = archiveBytes.LongLength,
            Sha256 = Hash(archiveBytes)
        },
        Files = files.Select(pair => new GameFile
        {
            Path = pair.Key,
            Size = pair.Value.LongLength,
            Sha256 = Hash(pair.Value)
        }).ToList()
    };

    private static byte[] CreateZip(IReadOnlyDictionary<string, byte[]> files, params (string Path, byte[] Content)[] additionalEntries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files) AddEntry(archive, pair.Key, pair.Value);
            foreach (var (path, content) in additionalEntries) AddEntry(archive, path, content);
        }
        return buffer.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(content);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Task<T> RunOnStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class FakeDistributionService : IDistributionService
    {
        private readonly byte[]? _packageBytes;
        private readonly Exception? _manifestError;
        public int ManifestRequests { get; private set; }
        public int PackageRequests { get; private set; }

        public FakeDistributionService() { }
        public FakeDistributionService(byte[] packageBytes) => _packageBytes = packageBytes;
        public FakeDistributionService(Exception manifestError) => _manifestError = manifestError;

        public Task<GameManifest> GetGameManifestAsync(CancellationToken cancellationToken = default)
        {
            ManifestRequests++;
            return _manifestError is not null
                ? Task.FromException<GameManifest>(_manifestError)
                : Task.FromException<GameManifest>(new InvalidOperationException("A test manifest was not configured."));
        }

        public Task<LauncherVersion?> GetLauncherVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult<LauncherVersion?>(null);

        public async Task DownloadGamePackageAsync(GamePackage package, string destination,
            IProgress<(long Received, long Total)>? progress, CancellationToken cancellationToken = default)
        {
            PackageRequests++;
            if (_packageBytes is null) throw new InvalidOperationException("Package downloads are not configured for this test.");
            await File.WriteAllBytesAsync(destination, _packageBytes, cancellationToken);
            progress?.Report((_packageBytes.LongLength, _packageBytes.LongLength));
        }

        public Task DownloadAsync(Uri uri, string destination, IProgress<(long Received, long Total)>? progress,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UBFLauncher.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
