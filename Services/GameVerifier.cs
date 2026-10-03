using System.Security.Cryptography;
using UBFLauncher.Models;

namespace UBFLauncher.Services;

public sealed class GameVerifier(Logger logger, FileVerificationOptions? options = null)
{
    private readonly FileVerificationOptions _options = options ?? new FileVerificationOptions();

    public async Task<IReadOnlyList<GameFile>> FindInvalidFilesAsync(string root, GameManifest manifest,
        IProgress<(string FileName, int Checked, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var invalid = new List<GameFile>();
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[i];
            var fullPath = ResolvePath(root, file.Path);
            var valid = false;
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Exists && info.Length == file.Size)
                {
                    if (!_options.VerifySha256)
                    {
                        valid = true;
                    }
                    else
                    {
                        var bufferSize = Math.Clamp(_options.HashBufferSizeKb, 32, 1024) * 1024;
                        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                        valid = Convert.ToHexString(hash).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (IOException ex) { logger.Error($"Could not verify {file.Path}", ex); }
            if (!valid) invalid.Add(file);
            progress?.Report((file.Path, i + 1, manifest.Files.Count));
        }
        return invalid;
    }

    public static string ResolvePath(string root, string relativePath)
    {
        if (!GitHubDistributionService.IsSafeRelativePath(relativePath)) throw new InvalidDataException($"Unsafe manifest path: {relativePath}");
        var fullRoot = Path.GetFullPath(root);
        var basePath = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Manifest path escapes the installation directory: {relativePath}");
        return fullPath;
    }
}
