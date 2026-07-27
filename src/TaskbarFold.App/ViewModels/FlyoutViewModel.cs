using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskbarFold.App.Services;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.ViewModels;

/// <summary>Backs one group's flyout. Kept alive between showings so opening is instant.</summary>
public sealed partial class FlyoutViewModel : ObservableObject
{
    private readonly IconCache _iconCache;
    private readonly ShortcutResolver _resolver;
    private readonly Launcher _launcher;
    private readonly Action _requestClose;
    private readonly Action _persistOrder;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private int _columns = 5;

    [ObservableProperty]
    private double _iconSize = 48;

    [ObservableProperty]
    private bool _showLabels = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private Brush _background = Brushes.Transparent;

    public FlyoutViewModel(
        Group group,
        IconCache iconCache,
        ShortcutResolver resolver,
        Launcher launcher,
        Action requestClose,
        Action persistOrder)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        _iconCache = iconCache ?? throw new ArgumentNullException(nameof(iconCache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _persistOrder = persistOrder ?? throw new ArgumentNullException(nameof(persistOrder));
    }

    public Group Group { get; private set; }

    /// <summary>Tiles currently on screen, already filtered by the search text.</summary>
    public ObservableCollection<ShortcutItemViewModel> VisibleItems { get; } = [];

    /// <summary>Every tile, in the group's own order.</summary>
    public List<ShortcutItemViewModel> AllItems { get; } = [];

    /// <summary>Whether launching an application also dismisses the flyout.</summary>
    public bool CloseOnLaunch { get; set; } = true;

    /// <summary>Whether typing a letter opens the filter box.</summary>
    public bool TypeToSearch { get; set; } = true;

    /// <summary>Rebuilds the tile list from the model. Cheap enough to run on every show.</summary>
    public void Load(Group group, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(settings);

        Group = group;
        Title = group.Name;
        Columns = group.Columns;
        IconSize = settings.IconSize;
        ShowLabels = settings.ShowLabels;
        CloseOnLaunch = settings.CloseOnLaunch;
        TypeToSearch = settings.TypeToSearch;
        Background = BuildBackground(group);
        ErrorMessage = null;
        SearchText = string.Empty;

        AllItems.Clear();

        for (var i = 0; i < group.Shortcuts.Count; i++)
        {
            var item = new ShortcutItemViewModel(group.Shortcuts[i]) { Ordinal = i + 1 };
            item.LoadIcon(_iconCache.GetOrCreate(group.Id, item.Model));
            item.ApplyResolution(_resolver.Resolve(item.Model));
            AllItems.Add(item);
        }

        ApplyFilter();
    }

    private static Brush BuildBackground(Group group)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(group.Background);
            if (converted is Color color)
            {
                var brush = new SolidColorBrush(
                    Color.FromArgb((byte)Math.Round(group.Opacity * 255), color.R, color.G, color.B));
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
        }

        var fallback = new SolidColorBrush(Color.FromArgb(235, 31, 31, 31));
        fallback.Freeze();
        return fallback;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        VisibleItems.Clear();

        var ordinal = 1;
        foreach (var item in AllItems)
        {
            item.IsVisible = item.Matches(SearchText);
            if (!item.IsVisible)
            {
                continue;
            }

            // Numbering follows what is on screen, so pressing 2 after typing always
            // launches the second tile the user can actually see.
            item.Ordinal = ordinal++;
            item.IsSelected = false;
            VisibleItems.Add(item);
        }

        if (VisibleItems.Count > 0)
        {
            VisibleItems[0].IsSelected = true;
        }

        IsEmpty = AllItems.Count == 0;
    }

    /// <summary>Moves selection by a grid step. Returns the newly selected tile.</summary>
    public ShortcutItemViewModel? MoveSelection(int delta)
    {
        if (VisibleItems.Count == 0)
        {
            return null;
        }

        var current = VisibleItems.ToList().FindIndex(i => i.IsSelected);
        if (current < 0)
        {
            current = 0;
        }

        var next = Math.Clamp(current + delta, 0, VisibleItems.Count - 1);

        foreach (var item in VisibleItems)
        {
            item.IsSelected = false;
        }

        VisibleItems[next].IsSelected = true;
        return VisibleItems[next];
    }

    public ShortcutItemViewModel? SelectedItem => VisibleItems.FirstOrDefault(i => i.IsSelected);

    [RelayCommand]
    public void Launch(ShortcutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var result = _launcher.Launch(item.Model);

        if (!result.Success)
        {
            // Stay open and explain, rather than vanishing and leaving the user guessing.
            ErrorMessage = result.Message;
            item.ApplyResolution(_resolver.Resolve(item.Model));
            return;
        }

        if (CloseOnLaunch)
        {
            _requestClose();
        }
    }

    /// <summary>Launches the tile carrying the given on-screen number, if there is one.</summary>
    public bool LaunchByOrdinal(int ordinal)
    {
        var item = VisibleItems.FirstOrDefault(i => i.Ordinal == ordinal);
        if (item is null)
        {
            return false;
        }

        Launch(item);
        return true;
    }

    [RelayCommand]
    private void Close() => _requestClose();

    /// <summary>
    /// Reorders a tile by drag and drop and writes the new order back to the model.
    /// Reordering in the flyout itself is the whole reason this exists — going through a
    /// settings window to move an icon one place left is the kind of friction that stops
    /// people from curating their groups at all.
    /// </summary>
    public void Reorder(ShortcutItemViewModel moved, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(moved);

        var from = AllItems.IndexOf(moved);
        if (from < 0)
        {
            return;
        }

        var to = Math.Clamp(targetIndex, 0, AllItems.Count - 1);
        if (from == to)
        {
            return;
        }

        AllItems.RemoveAt(from);
        AllItems.Insert(to, moved);

        Group.Shortcuts.Clear();
        Group.Shortcuts.AddRange(AllItems.Select(i => i.Model));

        ApplyFilter();
        _persistOrder();
    }

    /// <summary>Re-resolves a broken shortcut and keeps the fix if one was found.</summary>
    [RelayCommand]
    public void Rebind(ShortcutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_resolver.TryRebind(item.Model))
        {
            item.ApplyResolution(_resolver.Resolve(item.Model));
            item.LoadIcon(_iconCache.GetOrCreate(Group.Id, item.Model));
            ErrorMessage = null;
            _persistOrder();
        }
        else
        {
            ErrorMessage = $"{item.DisplayName} could not be found anywhere on this PC.";
        }
    }

    [RelayCommand]
    public void RunAsAdmin(ShortcutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var result = _launcher.Launch(item.Model, forceAsAdmin: true);
        if (!result.Success)
        {
            ErrorMessage = result.Message;
            return;
        }

        if (CloseOnLaunch)
        {
            _requestClose();
        }
    }

    [RelayCommand]
    public void OpenFileLocation(ShortcutItemViewModel? item)
    {
        if (item?.Model.Target is not { Length: > 0 } target)
        {
            return;
        }

        Launcher.RevealInExplorer(target);
        _requestClose();
    }

    [RelayCommand]
    public void ShowProperties(ShortcutItemViewModel? item)
    {
        if (item?.Model.Target is not { Length: > 0 } target)
        {
            return;
        }

        Launcher.ShowFileProperties(target);
        _requestClose();
    }

    [RelayCommand]
    public void RemoveFromGroup(ShortcutItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        AllItems.Remove(item);
        Group.Shortcuts.Remove(item.Model);
        _iconCache.Evict(Group.Id, item.Id);
        ApplyFilter();
        _persistOrder();
    }
}
