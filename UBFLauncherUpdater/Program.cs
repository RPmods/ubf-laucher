using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace UBFLauncherUpdater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 5 || args[0] != "--apply" || !int.TryParse(args[3], out var processId))
        {
            MessageBox.Show("No se pudieron leer los datos de actualización.", "UBF Launcher Updater", MessageBoxButton.OK, MessageBoxImage.Error);
            return 2;
        }

        var archive = Path.GetFullPath(args[1]);
        var target = Path.GetFullPath(args[2]);
        var temp = Path.GetFullPath(args[4]);
        var stage = Path.Combine(temp, "staged-files");
        var backup = Path.Combine(temp, "backup-files");
        try
        {
            if (!File.Exists(archive)) throw new FileNotFoundException("Update package was not found.", archive);
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Launcher installation directory was not found.");
            ExtractSafely(archive, stage);
            if (!File.Exists(Path.Combine(stage, "UBFLauncher.exe"))) throw new InvalidDataException("Update package does not contain UBFLauncher.exe.");

            try
            {
                if (!Process.GetProcessById(processId).WaitForExit(120_000)) throw new TimeoutException("El launcher no se cerró a tiempo para aplicar la actualización.");
            }
            catch (ArgumentException) { }

            var changed = new List<(string Destination, string? Backup)>();
            Directory.CreateDirectory(backup);
            try
            {
                foreach (var stagedPath in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(stage, stagedPath);
                    var destination = SafeCombine(target, relative);
                    var oldCopy = SafeCombine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    string? saved = null;
                    if (File.Exists(destination))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(oldCopy)!);
                        File.Move(destination, oldCopy, true);
                        saved = oldCopy;
                    }
                    try { File.Copy(stagedPath, destination, true); }
                    catch
                    {
                        if (saved is not null) File.Move(saved, destination, true);
                        throw;
                    }
                    changed.Add((destination, saved));
                }
            }
            catch
            {
                foreach (var (destination, saved) in changed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(destination)) File.Delete(destination);
                        if (saved is not null && File.Exists(saved)) File.Move(saved, destination, true);
                    }
                    catch { }
                }
                throw;
            }

            Process.Start(new ProcessStartInfo(Path.Combine(target, "UBFLauncher.exe")) { WorkingDirectory = target, UseShellExecute = true });
            ScheduleCleanup(temp);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo instalar la actualización del launcher. La versión anterior se conservará cuando sea posible.\n\n" + ex.Message,
                "UBF Launcher Updater", MessageBoxButton.OK, MessageBoxImage.Error);
            ScheduleCleanup(temp);
            return 1;
        }
    }

    private static void ExtractSafely(string archive, string stage)
    {
        Directory.CreateDirectory(stage);
        using var zip = ZipFile.OpenRead(archive);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            expanded = checked(expanded + entry.Length);
            if (expanded > 12L * 1024 * 1024 * 1024) throw new InvalidDataException("Update package expands beyond the allowed size.");
            var destination = SafeCombine(stage, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Replace('\\', '/').Split('/').Any(part => part is ".." or "." || part.Contains(':')))
            throw new InvalidDataException("Update archive contains an unsafe path.");
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update archive path escapes its target directory.");
        return full;
    }

    private static void ScheduleCleanup(string directory)
    {
        try
        {
            var escapedDirectory = directory.Replace("'", "''");
            var script = $"$updaterProcessId = {Environment.ProcessId}; $directory = '{escapedDirectory}'; while (Get-Process -Id $updaterProcessId -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 500 }}; Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }
}
