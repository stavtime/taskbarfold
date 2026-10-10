using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using TaskbarFold.App.Ipc;
using TaskbarFold.App.ViewModels;
using TaskbarFold.App.Views;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.Services;

/// <summary>
/// The resident. Owns every long-lived object: the config, the icon cache, one hidden
/// flyout window per group, the tray icon, the pipe listener and the global hotkeys.
///
/// Keeping the windows alive is the central design decision. taskbar-groups started a
/// fresh process on every click, so the first frame appeared only after a cold .NET
/// start; here the click is a pipe write plus a reposition.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly AppPaths _paths = AppPaths.Default;
    private readonly IGroupRepository _repository;
    private readonly IconCache _iconCache;
    private readonly ShortcutResolver _resolver;
    private readonly Launcher _launcher;
    private readonly PinService _pinService;
    private readonly AutoStartService _autoStart;
    private readonly AppxResolver _appProvider = new();
    private readonly ThemeService _theme = new();

    private readonly Dictionary<Guid, FlyoutWindow> _flyouts = [];

    /// <summary>Last known appearance of each group, so unchanged icons are left alone.</summary>
    private readonly Dictionary<Guid, string> _iconSignatures = [];

    private readonly System.Windows.Threading.DispatcherTimer _iconRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(600),
    };

    private PipeServer? _pipeServer;
    private ConfigWatcher? _configWatcher;
    private HotkeyService? _hotkeys;
    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsViewModel;
    private GroupsDocument _document = new();

    /// <summary>Where the last update notification should lead when clicked.</summary>
    private string? _pendingUpdateUrl;

    private bool _disposed;

    public AppHost()
    {
        _paths.EnsureCreated();
        Log.Initialize(_paths);

        _repository = new GroupRepository(_paths);
        _iconCache = new IconCache(_paths);
        _resolver = new ShortcutResolver();
        _launcher = new Launcher(_resolver);
        _pinService = new PinService(_paths);
        _autoStart = new AutoStartService();
    }

    public void Start(bool showSettings)
    {
        LoadConfiguration(initial: true);

        _theme.Apply(_document.Settings.Theme);
        _theme.EffectiveThemeChanged += (_, dark) =>
        {
            if (_settingsWindow is not null)
            {
                SystemBackdrop.SetDarkMode(_settingsWindow, dark);
            }
        };

        _iconRefreshTimer.Tick += (_, _) =>
        {
            _iconRefreshTimer.Stop();
            RefreshChangedIcons();
        };

        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += (_, groupId) =>
        {
            var (x, y) = TaskbarInfo.Instance.GetCursorPosition();
            ShowGroup(groupId, x, y);
        };

        _configWatcher = new ConfigWatcher(_repository.ConfigFilePath);
        _configWatcher.Changed += (_, _) => LoadConfiguration(initial: false);

        _pipeServer = new PipeServer(HandleMessage);
        _pipeServer.Faulted += (_, ex) => Log.Error("The request listener hit an error", ex);
        _pipeServer.Start();

        CreateTrayIcon();
        RegisterHotkeys();

        if (showSettings)
        {
            ShowSettings();
        }

        // Rendering every icon takes a moment on a cold cache. Doing it now, in the
        // background, means the first flyout of the session is as fast as the rest.
        var groups = _document.Groups.ToList();
        Task.Run(() =>
        {
            try
            {
                _iconCache.Warm(groups);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Icon cache warm-up stopped early: {ex.Message}");
            }
        });

        RepairPinnedIcons();

        ScheduleUpdateCheck();
    }

    // -- Configuration -------------------------------------------------------------------

    private void LoadConfiguration(bool initial)
    {
        var result = _repository.Load();
        _document = result.Document;

        switch (result.Status)
        {
            case ConfigLoadStatus.Recovered:
                Log.Error($"The configuration was unreadable and has been reset. Backup: {result.BackupPath ?? "none"}. {result.Error}");
                ShowTrayMessage("TaskbarFold", "Your configuration could not be read and has been reset. The damaged file was kept as a backup.");
                break;

            case ConfigLoadStatus.TooNew:
                Log.Error(result.Error ?? "The configuration was written by a newer version.");
                ShowTrayMessage("TaskbarFold", "This configuration was written by a newer version of TaskbarFold. It has been left untouched.");
                break;

            case ConfigLoadStatus.Migrated:
                Log.Info("The configuration was upgraded to the current schema.");
                _repository.Save(_document);
                break;
        }

        // Keep the auto-start entry and the stored preference in agreement — the user may
        // have removed the entry through Task Manager since the last run.
        if (_document.Settings.RunAtStartup != _autoStart.IsEnabled)
        {
            _document.Settings.RunAtStartup = _autoStart.IsEnabled;
        }

        // Record how each group currently looks, so the next edit is compared against the
        // configuration as loaded rather than against nothing — otherwise the first
        // unrelated change, such as switching theme, would rewrite every icon.
        _iconSignatures.Clear();
        foreach (var group in _document.Groups)
        {
            _iconSignatures[group.Id] = IconSignatureOf(group);
        }

        RebuildFlyouts();
        RegisterHotkeys();

        if (!initial)
        {
            _theme.Apply(_document.Settings.Theme);
            _settingsViewModel?.Reload(_document);
        }
    }

    /// <summary>
    /// Drops flyouts for groups that no longer exist and pre-creates windows for new ones.
    /// Existing windows are kept so their warm state survives a config reload.
    /// </summary>
    private void RebuildFlyouts()
    {
        var live = _document.Groups.Select(g => g.Id).ToHashSet();

        foreach (var (id, window) in _flyouts.Where(pair => !live.Contains(pair.Key)).ToList())
        {
            window.Dismiss();
            window.Close();
            _flyouts.Remove(id);
        }

        foreach (var group in _document.Groups)
        {
            if (_flyouts.ContainsKey(group.Id))
            {
                continue;
            }

            var viewModel = new FlyoutViewModel(
                group,
                _iconCache,
                _resolver,
                _launcher,
                requestClose: () => DismissFlyout(group.Id),
                persistOrder: SaveDocument);

            var window = new FlyoutWindow(viewModel);
            window.Prewarm();
            _flyouts[group.Id] = window;
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        var bindings = _document.Groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Hotkey))
            .Select(g => (g.Id, Gesture: g.Hotkey!));

        var failed = _hotkeys.Register(bindings);

        if (failed.Count > 0)
        {
            var names = _document.Groups
                .Where(g => failed.Contains(g.Id))
                .Select(g => g.Name);

            ShowTrayMessage("TaskbarFold", $"These hotkeys are already in use: {string.Join(", ", names)}");
        }
    }

    private void SaveDocument()
    {
        try
        {
            // Suppress first: the watcher would otherwise see our own write and reload,
            // rebuilding the very flyout the user is interacting with.
            _configWatcher?.SuppressNextChange();
            _repository.Save(_document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save the configuration", ex);
        }
    }

    // -- Requests from the signalling process -----------------------------------------------

    private string HandleMessage(PipeMessage message)
    {
        // The pipe listener runs on a background thread; everything below touches WPF.
        return Application.Current.Dispatcher.Invoke(() =>
        {
            switch (message.Command)
            {
                case PipeProtocol.ShowGroup when message.GroupId is { } groupId:
                    return ShowGroup(groupId, message.X, message.Y)
                        ? PipeProtocol.ResponseOk
                        : PipeProtocol.ResponseUnknownGroup;

                case PipeProtocol.OpenSettings:
                    ShowSettings();
                    return PipeProtocol.ResponseOk;

                case PipeProtocol.Ping:
                    return PipeProtocol.ResponseOk;

                default:
                    return PipeProtocol.ResponseError;
            }
        });
    }

    private bool ShowGroup(Guid groupId, int cursorX, int cursorY)
    {
        var group = _document.Find(groupId);
        if (group is null)
        {
            Log.Warn($"A shortcut asked for group {groupId}, which no longer exists.");
            return false;
        }

        if (!_flyouts.TryGetValue(groupId, out var window))
        {
            RebuildFlyouts();
            if (!_flyouts.TryGetValue(groupId, out window))
            {
                return false;
            }
        }

        // Clicking the same pinned button twice in a row should toggle, the way a real
        // taskbar button does.
        if (window.IsVisible)
        {
            window.Dismiss();
            return true;
        }

        foreach (var other in _flyouts.Values.Where(w => w.IsVisible))
        {
            other.Dismiss();
        }

        window.ShowAt(cursorX, cursorY, group, _document.Settings);
        return true;
    }

    private void DismissFlyout(Guid groupId)
    {
        if (_flyouts.TryGetValue(groupId, out var window))
        {
            window.Dismiss();
        }
    }

    // -- Settings window ----------------------------------------------------------------------

    public void ShowSettings()
    {
        if (_settingsViewModel is null)
        {
            _settingsViewModel = new SettingsViewModel(
                _repository, _iconCache, _resolver, _pinService, _autoStart, _appProvider, _document,
                onConfigChanged: OnSettingsChanged);
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel);
        }

        _settingsWindow.Show();

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void OnSettingsChanged()
    {
        _configWatcher?.SuppressNextChange();
        _theme.Apply(_document.Settings.Theme);
        RebuildFlyouts();
        RegisterHotkeys();

        // Typing a group name fires this on every keystroke. Rebuilding icons and
        // rewriting shortcuts that fast would be wasteful and would make the taskbar
        // flicker, so the refresh waits until the user stops.
        _iconRefreshTimer.Stop();
        _iconRefreshTimer.Start();
    }

    /// <summary>
    /// Rewrites the .ico of any group whose appearance changed.
    ///
    /// Without this, editing a group's emoji or colour updated the settings preview but
    /// left an already-pinned taskbar button showing the old picture until the user
    /// happened to press "Prepare shortcut" again.
    /// </summary>
    private void RefreshChangedIcons()
    {
        foreach (var group in _document.Groups)
        {
            var signature = IconSignatureOf(group);

            if (_iconSignatures.TryGetValue(group.Id, out var previous) && previous == signature)
            {
                continue;
            }

            _iconSignatures[group.Id] = signature;

            // Only groups that already have a generated icon are worth maintaining; for
            // the rest the icon is written on demand by PinService.PrepareLink.
            if (_pinService.HasGeneratedIcon(group.Id))
            {
                _pinService.RefreshIcon(group);
            }
        }
    }

    /// <summary>
    /// Brings every pinned group's icon back in line with its configuration at startup.
    ///
    /// The two can drift for reasons this process never observed: the config was edited
    /// or restored while the resident was not running, or a new build renders tiles
    /// differently. <see cref="PinService.RefreshIcon"/> is a no-op when nothing changed,
    /// so this costs a few milliseconds per group and silently fixes the rest.
    /// </summary>
    private void RepairPinnedIcons()
    {
        var groups = _document.Groups.ToList();

        Task.Run(() =>
        {
            foreach (var group in groups)
            {
                try
                {
                    _pinService.RefreshIcon(group);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                              or System.Runtime.InteropServices.COMException)
                {
                    Log.Warn($"Could not repair the icon for '{group.Name}': {ex.Message}");
                }
            }
        });
    }

    private static string IconSignatureOf(Group group) =>
        $"{group.Name}␟{group.Background}␟{group.Emoji}␟{group.IconPath}";

    // -- Tray ------------------------------------------------------------------------------------

    private void CreateTrayIcon()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();

        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = _autoStart.IsEnabled,
        };
        startupItem.Click += (_, _) =>
        {
            if (_autoStart.SetEnabled(startupItem.IsChecked))
            {
                _document.Settings.RunAtStartup = startupItem.IsChecked;
                SaveDocument();
                _settingsViewModel?.Reload(_document);
            }
            else
            {
                startupItem.IsChecked = _autoStart.IsEnabled;
            }
        };

        var folderItem = new MenuItem { Header = "Open the shortcuts folder" };
        folderItem.Click += (_, _) => _pinService.OpenLinksFolder();

        var refreshItem = new MenuItem { Header = "Refresh pinned taskbar icons" };
        refreshItem.Click += (_, _) => RefreshPinnedIcons();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settingsItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(folderItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "TaskbarFold",
            ContextMenu = menu,
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/TaskbarFold;component/Assets/TaskbarFold.ico", UriKind.Absolute)),
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowSettings();

        // A notification that cannot be acted on is a dead end: telling someone an update
        // exists and giving them nowhere to click is worse than saying nothing.
        _trayIcon.TrayBalloonTipClicked += (_, _) => OnNotificationClicked();

        _trayIcon.ForceCreate();
    }

    /// <summary>
    /// Rebuilds every group's icon, then offers to restart Explorer — the only thing that
    /// makes an already-pinned taskbar button drop its cached picture.
    /// </summary>
    private void RefreshPinnedIcons()
    {
        foreach (var group in _document.Groups)
        {
            _pinService.RefreshIcon(group);
        }

        var answer = MessageBox.Show(
            "Group icons have been rebuilt.\n\n" +
            "Windows keeps the picture of a button that is already pinned to the taskbar " +
            "in its own cache and will not reload it on its own. Restarting Explorer " +
            "applies the new icons immediately; it closes any open Explorer windows.\n\n" +
            "Restart Explorer now?",
            "TaskbarFold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ExplorerRefresh.RestartShell())
        {
            ShowTrayMessage("TaskbarFold", "Explorer could not be restarted. Signing out and back in also works.");
        }
    }

    /// <summary>
    /// Handles a click on the last notification. Only an update notification has
    /// somewhere to go; anything else just opens settings.
    /// </summary>
    private void OnNotificationClicked()
    {
        if (_pendingUpdateUrl is { Length: > 0 } url)
        {
            OpenUrl(url);
            return;
        }

        ShowSettings();
    }

    internal static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not open '{url}'", ex);
        }
    }

    private void ShowTrayMessage(string title, string message)
    {
        try
        {
            _trayIcon?.ShowNotification(title, message);
        }
        catch (InvalidOperationException)
        {
            // Notifications are disabled by policy on some machines; the log still has it.
            Log.Info($"{title}: {message}");
        }
    }

    // -- Updates -----------------------------------------------------------------------------------

    private void ScheduleUpdateCheck()
    {
        if (!_document.Settings.CheckForUpdates ||
            UpdatePolicy.IsCheckDisabled ||
            !UpdateChecker.IsDue(_document.Settings.LastUpdateCheckUtc))
        {
            return;
        }

        // Fire and forget on a background thread. Nothing on the UI path waits for this.
        _ = Task.Run(async () =>
        {
            var status = await new UpdateChecker().CheckAsync().ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Only a successful check counts against the daily throttle, otherwise a
                // machine that was offline this morning waits a further day for no reason.
                if (status.Result is not UpdateResult.Failed)
                {
                    _document.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
                    SaveDocument();
                }

                AnnounceUpdate(status);
            });
        });
    }

    /// <summary>Shows the update notification and arms the click that opens it.</summary>
    private void AnnounceUpdate(UpdateStatus status)
    {
        if (!status.IsAvailable || status.LatestVersion is not { } version)
        {
            return;
        }

        _pendingUpdateUrl = status.ReleaseUrl;

        var detail = string.IsNullOrWhiteSpace(status.Notes) ? string.Empty : $" {status.Notes}";
        var action = status.ReleaseUrl is null ? string.Empty : " Click to open the download page.";

        ShowTrayMessage("TaskbarFold", $"Version {version} is available.{detail}{action}");
        Log.Info($"Update available: {version} ({status.ReleaseUrl ?? "no link"})");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _iconRefreshTimer.Stop();
        _pipeServer?.Dispose();
        _configWatcher?.Dispose();
        _hotkeys?.Dispose();
        _theme.Dispose();

        _trayIcon?.Dispose();
        _trayIcon = null;

        foreach (var window in _flyouts.Values)
        {
            window.Close();
        }

        _flyouts.Clear();
        _settingsWindow?.Close();
    }
}
