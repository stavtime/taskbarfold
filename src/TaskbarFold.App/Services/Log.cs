using System.Globalization;
using System.IO;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.Services;

/// <summary>
/// Append-only log next to the config. Deliberately tiny: the resident runs unattended
/// for weeks, and when a flyout fails to open the user needs something to send along
/// with a bug report.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string _path = AppPaths.Default.LogFile;

    public static void Initialize(AppPaths paths) => _path = paths.LogFile;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Single rollover rather than a rotation scheme: one previous file is
                // enough context, and unbounded logs on a resident are a real problem.
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                File.AppendAllText(_path, $"[{stamp}] {level,-5} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that breaks the application.
        }
    }
}
