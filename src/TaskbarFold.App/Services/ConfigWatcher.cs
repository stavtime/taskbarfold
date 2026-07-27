using System.IO;
using System.Windows.Threading;

namespace TaskbarFold.App.Services;

/// <summary>
/// Watches <c>groups.json</c> and reports changes made from outside this process.
///
/// Two things make a naive FileSystemWatcher useless here: an atomic save fires several
/// events in a row, and this application's own writes would trigger a reload loop. So
/// events are debounced, and writes performed by the resident are suppressed explicitly.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _timer;
    private DateTime _suppressUntil = DateTime.MinValue;
    private bool _disposed;

    public ConfigWatcher(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);

        _timer = new DispatcherTimer { Interval = Debounce };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (DateTime.UtcNow >= _suppressUntil)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };

        var directory = Path.GetDirectoryName(configFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);

            _watcher = new FileSystemWatcher(directory, Path.GetFileName(configFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Could not watch the configuration file: {ex.Message}");
        }
    }

    /// <summary>Raised on the UI thread after the file settles.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Call around a save performed by this process so the resulting file events do not
    /// come back as an external change.
    /// </summary>
    public void SuppressNextChange() => _suppressUntil = DateTime.UtcNow.Add(Debounce + Debounce);

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Marshal onto the UI thread and restart the debounce window.
        _timer.Dispatcher.BeginInvoke(() =>
        {
            _timer.Stop();
            _timer.Start();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Dispose();
        }

        _disposed = true;
    }
}
