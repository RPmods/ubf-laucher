using System.Text.RegularExpressions;

namespace UBFLauncher.Services;

public static partial class VersionUtility
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    public static bool IsValid(string? value) => value is not null && VersionPattern().IsMatch(value);

    public static bool IsNewer(string remote, string? local)
    {
        if (!TryParse(remote, out var remoteVersion, out var remoteLabel)) return false;
        if (!TryParse(local, out var localVersion, out var localLabel)) return true;
        var comparison = remoteVersion.CompareTo(localVersion);
        if (comparison != 0) return comparison > 0;
        if (remoteLabel.Length == 0) return localLabel.Length > 0;
        if (localLabel.Length == 0) return false;
        return string.Compare(remoteLabel, localLabel, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static bool TryParse(string? value, out Version version, out string label)
    {
        version = new Version(0, 0, 0);
        label = "";
        if (!IsValid(value)) return false;
        var separator = value!.IndexOf('-');
        var numeric = separator < 0 ? value : value[..separator];
        label = separator < 0 ? "" : value[(separator + 1)..];
        return Version.TryParse(numeric, out version!);
    }
}
