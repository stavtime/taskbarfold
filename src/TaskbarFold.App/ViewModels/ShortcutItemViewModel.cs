using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.ViewModels;

/// <summary>One tile in the flyout, and one row in the group editor.</summary>
public sealed partial class ShortcutItemViewModel : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isBroken;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Position in the quick-launch order; 1–9 are reachable by number key.</summary>
    [ObservableProperty]
    private int _ordinal;

    public ShortcutItemViewModel(Shortcut model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Shortcut Model { get; }

    public Guid Id => Model.Id;

    public string DisplayName => Model.DisplayName;

    public string Tooltip => IsBroken && StatusMessage is not null
        ? $"{DisplayName} — {StatusMessage}"
        : DisplayName;

    /// <summary>True when the ordinal is worth painting on the tile.</summary>
    public bool HasQuickLaunchNumber => Ordinal is >= 1 and <= 9;

    public void ApplyResolution(ShortcutResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        IsBroken = !resolution.CanLaunch;
        StatusMessage = resolution.State == ShortcutState.Ok ? null : resolution.Message;
        OnPropertyChanged(nameof(Tooltip));
    }

    public void LoadIcon(string? pngPath)
    {
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
        {
            Icon = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();

            // OnLoad plus a stream copy: the default keeps the file locked, which would
            // stop the cache from refreshing the icon after an application update.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(pngPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            Icon = image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UriFormatException)
        {
            Icon = null;
        }
    }

    partial void OnOrdinalChanged(int value) => OnPropertyChanged(nameof(HasQuickLaunchNumber));

    /// <summary>Case- and position-insensitive match used by type-to-search.</summary>
    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
