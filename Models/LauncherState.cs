namespace UBFLauncher.Models;

public enum LauncherState
{
    CheckingLauncherUpdate, EnteringUsername, CheckingGame, NotInstalled, Verifying,
    Repairing, Downloading, Updating, ReadyToPlay, Launching, Error
}

public sealed record OperationProgress(string FileName, long BytesReceived, long TotalBytes,
    long TotalReceived, long TotalExpected, double BytesPerSecond)
{
    public double Percent => TotalExpected <= 0 ? 0 : Math.Clamp(TotalReceived * 100d / TotalExpected, 0, 100);
}
