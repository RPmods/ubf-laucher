using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using UBFLauncher.Models;

namespace UBFLauncher.Services;

public sealed class GameInstaller(IDistributionService distribution, GameVerifier verifier, Logger logger)
{
    private const string ManagedFilesName = ".ubf-installation.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int GetPendingCleanupCount(string root, GameManifest manifest)
    {
        var expected = GetManifestPaths(manifest);
        return GetManagedFiles(root).Count(path => !expected.Contains(path));
    }

    public async Task<int> InstallOrRepairAsync(string root, GameManifest manifest, IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(root);
        CleanStaleOperations(root);
        var installRoot = Path.GetFullPath(root);
        var expectedPaths = GetManifestPaths(manifest);
        var invalid = await verifier.FindInvalidFilesAsync(root, manifest, cancellationToken: cancellationToken);
        var obsolete = GetManagedFiles(installRoot).Where(path => !expectedPaths.Contains(path)).ToArray();
        if (invalid.Count == 0 && obsolete.Length == 0)
        {
            SaveManagedFiles(installRoot, manifest.Version, expectedPaths);
            return 0;
        }

        GamePackage? package = null;
        if (invalid.Count > 0)
        {
            package = manifest.Package ?? throw new InvalidDataException("The game manifest has no ZIP package metadata.");
            ValidatePackageMetadata(package);
        }

        var operation = Path.Combine(installRoot, ".ubf-staging-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(operation, "new");
        var packagePath = package is null ? null : Path.Combine(operation, package.FileName);
        var backup = Path.Combine(operation, "backup");
        Directory.CreateDirectory(staged);
        Directory.CreateDirectory(backup);
        var clock = Stopwatch.StartNew();
        var committed = new List<(string Destination, string? Backup)>();
        var preserveOperation = false;

        try
        {
            if (package is not null && packagePath is not null)
            {
                var downloadProgress = new Progress<(long Received, long Total)>(value =>
                {
                    var total = value.Total > 0 ? value.Total : package.Size;
                    progress?.Report(new OperationProgress(package.FileName, value.Received, total,
                        value.Received, package.Size, value.Received / Math.Max(clock.Elapsed.TotalSeconds, 0.1)));
                });
                logger.Info($"Downloading game ZIP package {package.FileName}");
                await distribution.DownloadGamePackageAsync(package, packagePath, downloadProgress, cancellationToken);

                var downloadedPackage = new FileInfo(packagePath);
                if (!downloadedPackage.Exists || downloadedPackage.Length != package.Size)
                    throw new InvalidDataException($"Game ZIP size does not match the manifest. Expected {package.Size} bytes.");

                await using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true))
                {
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Game ZIP failed SHA-256 validation.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await ExtractPackageSafelyAsync(packagePath, staged, manifest, cancellationToken);
                var invalidStagedFiles = await verifier.FindInvalidFilesAsync(staged, manifest, cancellationToken: cancellationToken);
                if (invalidStagedFiles.Count > 0)
                    throw new InvalidDataException($"The extracted package failed file verification: {string.Join(", ", invalidStagedFiles.Take(5).Select(file => file.Path))}");
                if (!manifest.Files.Any(file => file.Path.Equals("UBF.exe", StringComparison.OrdinalIgnoreCase)) ||
                    !File.Exists(GameVerifier.ResolvePath(staged, "UBF.exe")))
                    throw new InvalidDataException("The extracted game package does not contain UBF.exe at its root.");
            }

            foreach (var file in invalid)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GameVerifier.ResolvePath(installRoot, file.Path);
                var stagedPath = GameVerifier.ResolvePath(staged, file.Path);
                var backupPath = GameVerifier.ResolvePath(backup, file.Path);
                EnsureSafeParentDirectories(installRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                string? saved = null;
                if (File.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Move(destination, backupPath, true);
                    saved = backupPath;
                }

                try { File.Move(stagedPath, destination, true); }
                catch
                {
                    if (saved is not null) File.Move(saved, destination, true);
                    throw;
                }
                committed.Add((destination, saved));
            }

            foreach (var relativePath in obsolete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GameVerifier.ResolvePath(installRoot, relativePath);
                if (!File.Exists(destination)) continue;

                EnsureSafeParentDirectories(installRoot, relativePath);
                var backupPath = GameVerifier.ResolvePath(backup, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Move(destination, backupPath, true);
                committed.Add((destination, backupPath));
            }

            SaveManagedFiles(installRoot, manifest.Version, expectedPaths);

            logger.Info($"Applied {invalid.Count} game files and removed {obsolete.Length} obsolete managed files for version {manifest.Version}");
            if (package is not null)
            {
                progress?.Report(new OperationProgress(package.FileName, package.Size, package.Size,
                    package.Size, package.Size, package.Size / Math.Max(clock.Elapsed.TotalSeconds, 0.1)));
            }
            return invalid.Count + obsolete.Length;
        }
        catch (Exception operationError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var (destination, saved) in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    if (saved is not null && File.Exists(saved)) File.Move(saved, destination, true);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                    logger.Error($"Could not roll back {destination}; backup remains in {operation}", rollbackError);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                preserveOperation = true;
                throw new AggregateException($"Game installation failed and rollback was incomplete. Recovery files are preserved at {operation}.",
                    [operationError, .. rollbackErrors]);
            }

            throw;
        }
        finally
        {
            if (!preserveOperation)
            {
                try { if (Directory.Exists(operation)) Directory.Delete(operation, true); }
                catch (Exception ex) { logger.Error("Could not remove game staging directory", ex); }
            }
        }
    }

    private static void ValidatePackageMetadata(GamePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.FileName) ||
            !package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            package.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            package.FileName.Contains('/') || package.FileName.Contains('\\') ||
            package.FileName is "." or "..")
            throw new InvalidDataException("The game package must have a safe ZIP file name.");
        if (package.Size <= 0) throw new InvalidDataException("The game package must declare its exact positive size.");
        if (package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The game package must declare a valid SHA-256 hash.");
    }

    private IReadOnlyList<string> GetManagedFiles(string root)
    {
        var recordPath = Path.Combine(root, ManagedFilesName);
        if (!File.Exists(recordPath)) return [];
        try
        {
            var record = JsonSerializer.Deserialize<ManagedInstallationRecord>(File.ReadAllText(recordPath), JsonOptions);
            return record?.Files
                .Where(GitHubDistributionService.IsSafeRelativePath)
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (Exception ex)
        {
            logger.Error("Could not read the managed game file inventory; it will be rebuilt without removing unknown files", ex);
            return [];
        }
    }

    private static HashSet<string> GetManifestPaths(GameManifest manifest)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizePath(file.Path);
            if (!GitHubDistributionService.IsSafeRelativePath(normalized) || !paths.Add(normalized))
                throw new InvalidDataException($"Manifest contains an unsafe or duplicate path: {file.Path}");
        }
        return paths;
    }

    private static void SaveManagedFiles(string root, string version, IEnumerable<string> files)
    {
        var recordPath = Path.Combine(root, ManagedFilesName);
        var temporaryPath = recordPath + ".new-" + Guid.NewGuid().ToString("N");
        var record = new ManagedInstallationRecord
        {
            Version = version,
            Files = files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
        };
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, JsonOptions));
            File.Move(temporaryPath, recordPath, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    private void CleanStaleOperations(string root)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var path in Directory.EnumerateDirectories(root, ".ubf-staging-*", SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(path);
                var suffix = info.Name[".ubf-staging-".Length..];
                if (!Guid.TryParseExact(suffix, "N", out _) || info.LastWriteTimeUtc > cutoff ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                Directory.Delete(info.FullName, true);
                logger.Info($"Removed stale UBF staging directory {info.FullName}");
            }
        }
        catch (Exception ex)
        {
            logger.Error("Could not clean stale UBF staging directories", ex);
        }
    }

    private sealed class ManagedInstallationRecord
    {
        public string Version { get; set; } = "";
        public List<string> Files { get; set; } = [];
    }

    private static async Task ExtractPackageSafelyAsync(string archivePath, string destinationRoot, GameManifest manifest,
        CancellationToken cancellationToken)
    {
        var expectedFiles = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!GitHubDistributionService.IsSafeRelativePath(file.Path) || !expectedFiles.TryAdd(NormalizePath(file.Path), file))
                throw new InvalidDataException($"Manifest contains an unsafe or duplicate path: {file.Path}");
        }

        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = entry.FullName;
            var isDirectory = entryName.EndsWith("/", StringComparison.Ordinal);
            if (entryName.Contains('\\')) throw new InvalidDataException($"ZIP entry uses an unsafe path separator: {entryName}");
            var relativePath = isDirectory ? entryName.TrimEnd('/') : entryName;
            if (!GitHubDistributionService.IsSafeRelativePath(relativePath))
                throw new InvalidDataException($"ZIP contains an unsafe path: {entryName}");

            var normalizedPath = NormalizePath(relativePath);
            if (!seenEntries.Add(normalizedPath))
                throw new InvalidDataException($"ZIP contains a duplicate or case-colliding path: {entryName}");
            if (IsSymbolicLink(entry)) throw new InvalidDataException($"ZIP contains a symbolic link: {entryName}");

            EnsureParentsAreNotFiles(relativePath, expectedFiles, seenFiles);
            if (isDirectory)
            {
                if (expectedFiles.ContainsKey(normalizedPath))
                    throw new InvalidDataException($"ZIP path is both a file and a directory: {entryName}");
                seenDirectories.Add(normalizedPath);
                Directory.CreateDirectory(GameVerifier.ResolvePath(destinationRoot, relativePath));
                continue;
            }

            if (!expectedFiles.TryGetValue(normalizedPath, out var expected))
                throw new InvalidDataException($"ZIP contains a file not declared in the manifest: {relativePath}");
            if (seenDirectories.Contains(normalizedPath))
                throw new InvalidDataException($"ZIP path is both a file and a directory: {relativePath}");
            if (entry.Length != expected.Size)
                throw new InvalidDataException($"ZIP entry size does not match the manifest: {relativePath}");

            var destination = GameVerifier.ResolvePath(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written = checked(written + read);
                if (written > expected.Size)
                    throw new InvalidDataException($"ZIP entry expands beyond its manifest size: {relativePath}");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (written != expected.Size)
                throw new InvalidDataException($"ZIP entry is truncated: {relativePath}");
            await output.FlushAsync(cancellationToken);
            seenFiles.Add(normalizedPath);
        }

        var missingFiles = expectedFiles.Keys.Where(path => !seenFiles.Contains(path)).Take(5).ToArray();
        if (missingFiles.Length > 0)
            throw new InvalidDataException($"ZIP is missing files declared in the manifest: {string.Join(", ", missingFiles)}");
    }

    private static void EnsureParentsAreNotFiles(string relativePath, IReadOnlyDictionary<string, GameFile> expectedFiles,
        HashSet<string> seenFiles)
    {
        var segments = NormalizePath(relativePath).Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            var parent = string.Join('/', segments.Take(i));
            if (expectedFiles.ContainsKey(parent) || seenFiles.Contains(parent))
                throw new InvalidDataException($"ZIP path uses a file as a directory: {parent}");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixFileType == 0xA000;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static void EnsureSafeParentDirectories(string root, string relativePath)
    {
        var segments = NormalizePath(relativePath).Split('/');
        var current = Path.GetFullPath(root);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Installation path contains a symbolic link or junction: {current}");
        }
    }
}
