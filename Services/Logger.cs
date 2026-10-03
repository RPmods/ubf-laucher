namespace UBFLauncher.Services;

public sealed class Logger
{
    private readonly string _path;
    private readonly object _gate = new();

    public Logger(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UBFLauncher", "Logs", "launcher.log");
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{(exception is null ? "" : Environment.NewLine + exception)}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
