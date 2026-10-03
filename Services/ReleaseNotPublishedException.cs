namespace UBFLauncher.Services;

public sealed class ReleaseNotPublishedException(string message) : Exception(message);
