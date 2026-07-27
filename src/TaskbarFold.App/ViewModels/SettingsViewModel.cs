using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskbarFold.App.Services;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.ViewModels;

/// <summary>Backs the settings window: the group list, the editor, and app preferences.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IGroupRepository _repository;
    private readonly IconCache _iconCache;
    private readonly ShortcutResolver _resolver;
    private readonly PinService _pinService;
    private readonly AutoStartService _autoStart;
    private readonly IInstalledAppProvider _appProvider;
    private readonly Action _onConfigChanged;

    private GroupsDocument _document;
    private bool _suspendSave;

    [ObservableProperty]
    private GroupEditorViewModel? _selectedGroup;

    [ObservableProperty]
    private ShortcutItemViewModel? _selectedShortcut;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _pinHint;

    [ObservableProperty]
    private bool _isLoadingApps;

    [ObservableProperty]
    private string _appSearchText = string.Empty;

    public SettingsViewModel(
        IGroupRepository repository,
        IconCache iconCache,
        ShortcutResolver resolver,
        PinService pinService,
        AutoStartService autoStart,
        IInstalledAppProvider appProvider,
        GroupsDocument document,
        Action onConfigChanged)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _iconCache = iconCache ?? throw new ArgumentNullException(nameof(iconCache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _pinService = pinService ?? throw new ArgumentNullException(nameof(pinService));
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _appProvider = appProvider ?? throw new ArgumentNullException(nameof(appProvider));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _onConfigChanged = onConfigChanged ?? throw new ArgumentNullException(nameof(onConfigChanged));

        Reload(document);
    }

    public ObservableCollection<GroupEditorViewModel> Groups { get; } = [];

    /// <summary>Installed applications offered by the picker, filtered by the search box.</summary>
    public ObservableCollection<InstalledApp> AvailableApps { get; } = [];

    /// <summary>Quick-pick emoji for the group tile, each with a pre-rendered image.</summary>
    public ObservableCollection<EmojiSuggestion> EmojiSuggestions { get; } = [];

    /// <summary>
    /// Renders the emoji palette. Direct2D factory setup dominates the cost, so the whole
    /// palette is drawn in one batch, off the UI thread.
    /// </summary>
    private async Task LoadEmojiSuggestionsAsync()
    {
        if (EmojiSuggestions.Count > 0 || !EmojiRenderer.IsSupported)
        {
            return;
        }

        try
        {
            var wanted = EmojiCatalog.Defaults.Select(d => d.Emoji).ToList();
            var rendered = await Task.Run(() => EmojiRenderer.RenderMany(wanted, 48)).ConfigureAwait(true);

            foreach (var (emoji, name) in EmojiCatalog.Defaults)
            {
                if (!rendered.TryGetValue(emoji, out var png))
                {
                    continue;
                }

                EmojiSuggestions.Add(new EmojiSuggestion(emoji, name, CreateImage(png)));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Could not render the emoji palette: {ex.Message}");
        }
    }

    private static System.Windows.Media.ImageSource? CreateImage(byte[] png)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png, writable: false);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private List<InstalledApp> _allApps = [];

    public IReadOnlyList<int> ColumnChoices { get; } = [3, 4, 5, 6, 7, 8];

    public IReadOnlyList<string> ColorChoices { get; } = GroupIconRenderer.SuggestedColors;

    public IReadOnlyList<AppTheme> ThemeChoices { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    // -- Application-wide preferences ------------------------------------------------

    public AppTheme Theme
    {
        get => _document.Settings.Theme;
        set
        {
            if (_document.Settings.Theme == value)
            {
                return;
            }

            _document.Settings.Theme = value;
            OnPropertyChanged();
            Save();
        }
    }

    public int IconSize
    {
        get => _document.Settings.IconSize;
        set
        {
            if (_document.Settings.IconSize == value)
            {
                return;
            }

            _document.Settings.IconSize = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool ShowLabels
    {
        get => _document.Settings.ShowLabels;
        set
        {
            if (_document.Settings.ShowLabels == value)
            {
                return;
            }

            _document.Settings.ShowLabels = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool CloseOnLaunch
    {
        get => _document.Settings.CloseOnLaunch;
        set
        {
            if (_document.Settings.CloseOnLaunch == value)
            {
                return;
            }

            _document.Settings.CloseOnLaunch = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool TypeToSearch
    {
        get => _document.Settings.TypeToSearch;
        set
        {
            if (_document.Settings.TypeToSearch == value)
            {
                return;
            }

            _document.Settings.TypeToSearch = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool CheckForUpdates
    {
        get => _document.Settings.CheckForUpdates;
        set
        {
            if (_document.Settings.CheckForUpdates == value)
            {
                return;
            }

            _document.Settings.CheckForUpdates = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool RunAtStartup
    {
        get => _document.Settings.RunAtStartup;
        set
        {
            if (_document.Settings.RunAtStartup == value)
            {
                return;
            }

            if (!_autoStart.SetEnabled(value))
            {
                StatusMessage = "Windows refused to change the startup entry.";
                return;
            }

            _document.Settings.RunAtStartup = value;
            OnPropertyChanged();
            Save();
        }
    }

    // -- Group management -------------------------------------------------------------

    [RelayCommand]
    private void AddGroup()
    {
        var group = new Group
        {
            Name = $"Group {Groups.Count + 1}",
            Background = GroupIconRenderer.SuggestedColors[Groups.Count % GroupIconRenderer.SuggestedColors.Length],
        };

        _document.Groups.Add(group);
        var editor = CreateEditor(group);
        Groups.Add(editor);
        SelectedGroup = editor;
        Save();
    }

    [RelayCommand]
    private void DuplicateGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var copy = SelectedGroup.Model.Clone();
        _document.Groups.Add(copy);
        var editor = CreateEditor(copy);
        Groups.Add(editor);
        SelectedGroup = editor;
        Save();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var editor = SelectedGroup;
        _document.Groups.Remove(editor.Model);
        Groups.Remove(editor);

        // The pinned .lnk and the cached icons are ours; leaving them behind would put a
        // dead button on the taskbar that silently does nothing.
        _iconCache.Evict(editor.Id);
        _pinService.CleanUp(editor.Model);

        SelectedGroup = Groups.FirstOrDefault();
        Save();
    }

    [RelayCommand]
    private void PinSelectedGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var preparation = _pinService.PrepareLink(SelectedGroup.Model);
        PinHint = preparation.Message;

        if (preparation is { Success: true, LinkPath: { } path })
        {
            PinService.RevealLink(path);
        }
    }

    // -- Shortcut management ----------------------------------------------------------

    /// <summary>Adds file system paths, e.g. from a file dialog or an Explorer drop.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (SelectedGroup is null)
        {
            StatusMessage = "Select a group first.";
            return;
        }

        var added = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                continue;
            }

            SelectedGroup.Add(Shortcut.FromPath(path));
            added++;
        }

        if (added > 0)
        {
            StatusMessage = added == 1 ? "Added 1 shortcut." : $"Added {added} shortcuts.";
            Save();
        }
    }

    public void AddInstalledApp(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (SelectedGroup is null)
        {
            StatusMessage = "Select a group first.";
            return;
        }

        // Packaged apps carry only an AUMID; classic entries in the Apps folder also
        // resolve through it, so both are stored the same way.
        SelectedGroup.Add(new Shortcut
        {
            DisplayName = app.DisplayName,
            AppUserModelId = app.AppUserModelId,
        });

        StatusMessage = $"Added {app.DisplayName}.";
        Save();
    }

    [RelayCommand]
    private void RemoveShortcut()
    {
        if (SelectedGroup is null || SelectedShortcut is null)
        {
            return;
        }

        SelectedGroup.Remove(SelectedShortcut);
        SelectedShortcut = null;
        Save();
    }

    [RelayCommand]
    private void MoveShortcutUp() => MoveSelectedShortcut(-1);

    [RelayCommand]
    private void MoveShortcutDown() => MoveSelectedShortcut(1);

    private void MoveSelectedShortcut(int delta)
    {
        if (SelectedGroup is null || SelectedShortcut is null)
        {
            return;
        }

        var index = SelectedGroup.Shortcuts.IndexOf(SelectedShortcut);
        SelectedGroup.Move(index, index + delta);
    }

    /// <summary>Called by the view after an inline edit so the icon and status refresh.</summary>
    public void RefreshSelectedShortcut()
    {
        if (SelectedGroup is not null && SelectedShortcut is not null)
        {
            SelectedGroup.Refresh(SelectedShortcut);
            Save();
        }
    }

    // -- Installed apps picker ---------------------------------------------------------

    [RelayCommand]
    private async Task LoadInstalledAppsAsync()
    {
        // The emoji palette is cheap next to the app enumeration; both are warmed here so
        // the group editor is fully populated by the time the user reaches it.
        await LoadEmojiSuggestionsAsync().ConfigureAwait(true);

        if (IsLoadingApps || _allApps.Count > 0)
        {
            return;
        }

        IsLoadingApps = true;
        try
        {
            // Enumerating the Apps folder walks every installed package; doing it on the
            // UI thread would freeze the window for a second or more on a full machine.
            _allApps = await Task.Run(() => _appProvider.GetInstalledApps().ToList()).ConfigureAwait(true);
            FilterApps();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Error("Could not enumerate installed applications", ex);
            StatusMessage = "The list of installed applications could not be read.";
        }
        finally
        {
            IsLoadingApps = false;
        }
    }

    partial void OnAppSearchTextChanged(string value) => FilterApps();

    private void FilterApps()
    {
        AvailableApps.Clear();

        var query = AppSearchText;
        foreach (var app in _allApps)
        {
            // The application id is matched as well as the display name, because the two
            // often disagree: Codex is listed as "ChatGPT", and its identity
            // "OpenAI.Codex_..." is the only place the name the user is looking for appears.
            if (string.IsNullOrWhiteSpace(query) ||
                app.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                app.AppUserModelId.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                AvailableApps.Add(app);
            }
        }
    }

    // -- Updates ---------------------------------------------------------------------

    [ObservableProperty]
    private string? _updateStatusText;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>Set when a newer release exists, so the view can offer a way to open it.</summary>
    [ObservableProperty]
    private string? _updateUrl;

    public string CurrentVersionText => $"Version {UpdateChecker.CurrentVersionDisplay}";

    /// <summary>True when an administrator has taken update checking out of the user's hands.</summary>
    public bool UpdatesManagedByPolicy => UpdatePolicy.IsCheckDisabled;

    [RelayCommand]
    private async Task CheckForUpdatesNowAsync()
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateUrl = null;
        UpdateStatusText = "Checking…";

        try
        {
            // Deliberately bypasses the once-a-day throttle: the user asked, right now.
            var status = await new UpdateChecker().CheckAsync().ConfigureAwait(true);

            UpdateStatusText = status.Result switch
            {
                UpdateResult.Available => $"Version {status.LatestVersion} is available.",
                UpdateResult.UpToDate => "You have the latest version.",
                UpdateResult.Disabled => "Updates are managed by your administrator.",
                _ => "Could not reach the update server.",
            };

            if (status.IsAvailable)
            {
                UpdateUrl = status.ReleaseUrl;

                if (!string.IsNullOrWhiteSpace(status.Notes))
                {
                    UpdateStatusText += $" {status.Notes}";
                }
            }

            if (status.Result is not UpdateResult.Failed)
            {
                _document.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
                Save();
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (UpdateUrl is { Length: > 0 } url)
        {
            AppHost.OpenUrl(url);
        }
    }

    // -- Persistence -------------------------------------------------------------------

    /// <summary>Replaces the in-memory document, e.g. after the file changed on disk.</summary>
    public void Reload(GroupsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _suspendSave = true;
        try
        {
            _document = document;
            var previousId = SelectedGroup?.Id;

            Groups.Clear();
            foreach (var group in document.Groups)
            {
                Groups.Add(CreateEditor(group));
            }

            SelectedGroup = Groups.FirstOrDefault(g => g.Id == previousId) ?? Groups.FirstOrDefault();

            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(IconSize));
            OnPropertyChanged(nameof(ShowLabels));
            OnPropertyChanged(nameof(CloseOnLaunch));
            OnPropertyChanged(nameof(TypeToSearch));
            OnPropertyChanged(nameof(CheckForUpdates));
            OnPropertyChanged(nameof(RunAtStartup));
        }
        finally
        {
            _suspendSave = false;
        }
    }

    public GroupsDocument Document => _document;

    private GroupEditorViewModel CreateEditor(Group group) =>
        new(group, _iconCache, _resolver, Save);

    private void Save()
    {
        if (_suspendSave)
        {
            return;
        }

        try
        {
            _repository.Save(_document);
            _onConfigChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save the configuration", ex);
            StatusMessage = "Settings could not be saved. Check that %APPDATA% is writable.";
        }
    }
}
