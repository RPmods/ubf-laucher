using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Collections;
using System.Resources;
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
    public async Task LauncherUpdateWithoutANewerVersionReenablesControls()
    {
        using var temp = new TemporaryDirectory();
        var logger = new Logger(Path.Combine(temp.Path, "launcher-noop.log"));
        var config = new LauncherConfig { InstallDirectory = Path.Combine(temp.Path, "game") };
        var remote = new FakeDistributionService(new LauncherVersion { Version = "1.0.17" });
        using var audio = new AudioService(config, logger);
        var verifier = new GameVerifier(logger);
        var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

        await viewModel.UpdateLauncherAsync();

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanVerify);
    }

    [Fact]
    public async Task LauncherUpdateFailureKeepsTheLocalGamePlayable()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(Path.Combine(root, "UBF", "Binaries", "Win64"));
        await File.WriteAllBytesAsync(Path.Combine(root, "UBF.exe"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(root, "UBF", "Binaries", "Win64", "UBF-Win64-Shipping.exe"), [1]);
        var logger = new Logger(Path.Combine(temp.Path, "launcher-failure.log"));
        var config = new LauncherConfig { InstallDirectory = root, InstalledGameVersion = "1.0.4-beta" };
        var remote = new FakeDistributionService(new LauncherVersion { Version = "1.0.18" });
        using var audio = new AudioService(config, logger);
        var verifier = new GameVerifier(logger);
        var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

        await viewModel.CheckGamePresenceAsync();
        await viewModel.UpdateLauncherAsync();

        Assert.Equal(LauncherState.ReadyToPlay, viewModel.State);
        Assert.Equal("JUGAR", viewModel.PrimaryAction);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RepeatedVerificationNeverDownloadsTheGamePackage()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(root);
        var executable = new byte[] { 41, 42, 43 };
        await File.WriteAllBytesAsync(Path.Combine(root, "UBF.exe"), [0]);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["UBF.exe"] = executable };
        var archive = CreateZip(files);
        var manifest = CreateManifest(archive, files);
        var remote = new FakeDistributionService(manifest);
        var logger = new Logger(Path.Combine(temp.Path, "verify-repeat.log"));

        await RunOnStaAsync(() =>
        {
            var config = new LauncherConfig { InstallDirectory = root, InstalledGameVersion = manifest.Version };
            using var audio = new AudioService(config, logger);
            var verifier = new GameVerifier(logger);
            var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

            viewModel.VerifyGameAsync().GetAwaiter().GetResult();
            viewModel.VerifyGameAsync().GetAwaiter().GetResult();

            Assert.Equal(2, remote.ManifestRequests);
            Assert.Equal(0, remote.PackageRequests);
            Assert.Equal(LauncherState.Repairing, viewModel.State);
            Assert.Equal("REPARAR", viewModel.PrimaryAction);
            return true;
        });
    }

    [Fact]
    public async Task UpdatingRemovesOnlyPreviouslyManagedObsoleteFiles()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        var firstFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBF.exe"] = [1, 2, 3],
            ["Content/Legacy.pak"] = [4, 5, 6]
        };
        var firstArchive = CreateZip(firstFiles);
        await CreateInstaller(temp.Path, firstArchive).InstallOrRepairAsync(root, CreateManifest(firstArchive, firstFiles), null);

        var playerSave = Path.Combine(root, "Saved", "player.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(playerSave)!);
        await File.WriteAllTextAsync(playerSave, "keep this player data");

        var nextFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["UBF.exe"] = [7, 8, 9],
            ["Content/Current.pak"] = [10, 11, 12]
        };
        var nextArchive = CreateZip(nextFiles);
        var nextManifest = CreateManifest(nextArchive, nextFiles);
        var installer = CreateInstaller(temp.Path, nextArchive);

        Assert.Equal(3, await installer.InstallOrRepairAsync(root, nextManifest, null));
        Assert.False(File.Exists(Path.Combine(root, "Content", "Legacy.pak")));
        Assert.Equal(nextFiles["UBF.exe"], await File.ReadAllBytesAsync(Path.Combine(root, "UBF.exe")));
        Assert.Equal(nextFiles["Content/Current.pak"], await File.ReadAllBytesAsync(Path.Combine(root, "Content", "Current.pak")));
        Assert.Equal("keep this player data", await File.ReadAllTextAsync(playerSave));
        Assert.Equal(0, installer.GetPendingCleanupCount(root, nextManifest));
    }

    [Fact]
    public async Task DownloadClosesTheTemporaryFileBeforeReplacingDestination()
    {
        using var temp = new TemporaryDirectory();
        var destination = Path.Combine(temp.Path, "UBFLauncher-update.zip");
        var expected = new byte[] { 19, 23, 29, 31 };
        var logger = new Logger(Path.Combine(temp.Path, "download.log"));
        var service = new GitHubDistributionService(new LauncherConfig(), logger, new StaticHttpMessageHandler(expected));

        await service.DownloadAsync(new Uri("https://updates.example.invalid/UBFLauncher-update.zip"), destination, null);

        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.download-*"));
    }

    [Fact]
    public async Task UnrealProjectBinariesFolderIsRejectedAsGameInstallDirectory()
    {
        using var temp = new TemporaryDirectory();
        var project = Path.Combine(temp.Path, "UBF");
        var output = Path.Combine(project, "Binaries", "Win64");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(project, "UBF.uproject"), "{}");
        await File.WriteAllTextAsync(Path.Combine(output, "UBF.exe"), "development build");
        var logger = new Logger(Path.Combine(temp.Path, "invalid-path.log"));
        var remote = new FakeDistributionService();

        await RunOnStaAsync(() =>
        {
            var config = new LauncherConfig { InstallDirectory = output };
            using var audio = new AudioService(config, logger);
            var verifier = new GameVerifier(logger);
            var viewModel = new MainViewModel(config, remote, verifier, new GameInstaller(remote, verifier, logger), audio, logger);

            viewModel.CheckGamePresenceAsync().GetAwaiter().GetResult();
            Assert.Equal(LauncherState.NotInstalled, viewModel.State);
            Assert.Equal("INSTALAR", viewModel.PrimaryAction);
            viewModel.InstallOrUpdateGameAsync(output).GetAwaiter().GetResult();
            Assert.Equal(output, config.InstallDirectory);
            Assert.Equal(0, remote.ManifestRequests);
            Assert.Equal(0, remote.PackageRequests);
            return true;
        });
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
    public void TwitchVectorAssetLoadsFromLauncherResources()
    {
        Assert.Contains("assets/twitchmark.baml", LoadCompiledResourceKeys());
    }

    [Fact]
    public void SocialAndAudioVectorAssetsLoadFromLauncherResources()
    {
        var keys = LoadCompiledResourceKeys();
        Assert.Contains("assets/socialmarks.baml", keys);
        Assert.Contains("assets/audiomarks.baml", keys);
    }

    private static HashSet<string> LoadCompiledResourceKeys()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("UBFLauncher.g.resources")
            ?? throw new InvalidDataException("Compiled WPF resources are missing.");
        using var reader = new ResourceReader(stream);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = reader.GetEnumerator();
        while (entries.MoveNext()) keys.Add((string)entries.Key);
        return keys;
    }

    [Fact]
    public async Task LocalPlayRemainsAvailableWhenRemoteManifestIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "game");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "UBF.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), executable);
        var shippingDirectory = Path.Combine(root, "UBF", "Binaries", "Win64");
        Directory.CreateDirectory(shippingDirectory);
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(shippingDirectory, "UBF-Win64-Shipping.exe"));
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
            Assert.Equal(LauncherState.Launching, viewModel.State);
            Assert.Equal("INICIANDO...", viewModel.PrimaryAction);
            Assert.False(viewModel.CanUsePrimaryAction);
            Assert.False(viewModel.CanVerify);
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
        private readonly GameManifest? _gameManifest;
        private readonly LauncherVersion? _launcherVersion;
        public int ManifestRequests { get; private set; }
        public int PackageRequests { get; private set; }

        public FakeDistributionService() { }
        public FakeDistributionService(byte[] packageBytes) => _packageBytes = packageBytes;
        public FakeDistributionService(Exception manifestError) => _manifestError = manifestError;
        public FakeDistributionService(GameManifest manifest) => _gameManifest = manifest;
        public FakeDistributionService(LauncherVersion launcherVersion) => _launcherVersion = launcherVersion;

        public Task<GameManifest> GetGameManifestAsync(CancellationToken cancellationToken = default)
        {
            ManifestRequests++;
            return _manifestError is not null
                ? Task.FromException<GameManifest>(_manifestError)
                : _gameManifest is not null
                    ? Task.FromResult(_gameManifest)
                    : Task.FromException<GameManifest>(new InvalidOperationException("A test manifest was not configured."));
        }

        public Task<LauncherVersion?> GetLauncherVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult(_launcherVersion);

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

    private sealed class StaticHttpMessageHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UBFLauncher.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
