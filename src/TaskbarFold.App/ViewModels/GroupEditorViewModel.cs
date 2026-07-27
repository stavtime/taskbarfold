using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.ViewModels;

/// <summary>
/// Editable view over one <see cref="Group"/>. Writes straight through to the model so
/// the live preview and the saved document never disagree.
/// </summary>
public sealed partial class GroupEditorViewModel : ObservableObject
{
    private readonly IconCache _iconCache;
    private readonly ShortcutResolver _resolver;
    private readonly Action _onChanged;

    public GroupEditorViewModel(
        Group model, IconCache iconCache, ShortcutResolver resolver, Action onChanged)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _iconCache = iconCache ?? throw new ArgumentNullException(nameof(iconCache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var shortcut in model.Shortcuts)
        {
            Shortcuts.Add(CreateItem(shortcut));
        }

        Renumber();
    }

    public Group Model { get; }

    public Guid Id => Model.Id;

    public ObservableCollection<ShortcutItemViewModel> Shortcuts { get; } = [];

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));

            // The tile falls back to the group's initial, so a rename can change it.
            InvalidateTile();
            _onChanged();
        }
    }

    public int Columns
    {
        get => Model.Columns;
        set
        {
            if (Model.Columns == value)
            {
                return;
            }

            Model.Columns = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Background
    {
        get => Model.Background;
        set
        {
            if (Model.Background == value)
            {
                return;
            }

            Model.Background = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundBrush));
            InvalidateTile();
            _onChanged();
        }
    }

    public double Opacity
    {
        get => Model.Opacity;
        set
        {
            if (Math.Abs(Model.Opacity - value) < 0.001)
            {
                return;
            }

            Model.Opacity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundBrush));
            _onChanged();
        }
    }

    public string? IconPath
    {
        get => Model.IconPath;
        set
        {
            if (Model.IconPath == value)
            {
                return;
            }

            Model.IconPath = value;
            OnPropertyChanged();
            InvalidateTile();
            _onChanged();
        }
    }

    public string? Emoji
    {
        get => Model.Emoji;
        set
        {
            // Keep only the first complete emoji: a pasted string of several would draw
            // a row of tiny glyphs where one large one belongs.
            var normalized = EmojiRenderer.FirstEmoji(value);
            if (normalized is not null && !EmojiRenderer.LooksLikeEmoji(normalized))
            {
                normalized = null;
            }

            if (Model.Emoji == normalized)
            {
                return;
            }

            Model.Emoji = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEmoji));
            InvalidateTile();
            _onChanged();
        }
    }

    public bool HasEmoji => !string.IsNullOrEmpty(Model.Emoji);

    /// <summary>
    /// The group tile exactly as it will be written into the .ico. Rendered through the
    /// same code path rather than reproduced with WPF text, which cannot draw colour
    /// emoji at all.
    /// </summary>
    public ImageSource? TileImage
    {
        get
        {
            if (_tileImage is not null)
            {
                return _tileImage;
            }

            try
            {
                // A user-supplied image wins over the emoji, matching the order
                // PinService.EnsureIcon uses when it writes the .ico.
                _tileImage = !string.IsNullOrWhiteSpace(Model.IconPath) && File.Exists(Model.IconPath)
                    ? LoadFromFile(Model.IconPath)
                    : LoadFromBytes(
                        GroupIconRenderer.RenderGroupTilePng(96, Model.Name, Model.Background, Model.Emoji));
            }
            catch (Exception ex) when (ex is NotSupportedException or ExternalException or IOException)
            {
                _tileImage = null;
            }

            return _tileImage;
        }
    }

    private ImageSource? _tileImage;

    private static ImageSource LoadFromBytes(byte[] png)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png, writable: false);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource LoadFromFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();

        // OnLoad plus IgnoreImageCache so replacing the file is picked up and the file
        // itself is not left locked by the preview.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void InvalidateTile()
    {
        _tileImage = null;
        OnPropertyChanged(nameof(TileImage));
    }

    public string? Hotkey
    {
        get => Model.Hotkey;
        set
        {
            if (Model.Hotkey == value)
            {
                return;
            }

            Model.Hotkey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplay));
            _onChanged();
        }
    }

    public string HotkeyDisplay => string.IsNullOrWhiteSpace(Hotkey) ? "Not set" : Hotkey;

    public string Summary => Shortcuts.Count switch
    {
        0 => "No shortcuts yet",
        1 => "1 shortcut",
        _ => $"{Shortcuts.Count} shortcuts",
    };

    public Brush BackgroundBrush
    {
        get
        {
            try
            {
                if (ColorConverter.ConvertFromString(Model.Background) is Color color)
                {
                    var brush = new SolidColorBrush(
                        Color.FromArgb((byte)Math.Round(Model.Opacity * 255), color.R, color.G, color.B));
                    brush.Freeze();
                    return brush;
                }
            }
            catch (FormatException)
            {
            }

            return Brushes.DimGray;
        }
    }

    public ShortcutItemViewModel Add(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        Model.Shortcuts.Add(shortcut);
        var item = CreateItem(shortcut);
        Shortcuts.Add(item);
        Renumber();
        _onChanged();
        return item;
    }

    public void Remove(ShortcutItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Model.Shortcuts.Remove(item.Model);
        Shortcuts.Remove(item);
        _iconCache.Evict(Model.Id, item.Id);
        Renumber();
        _onChanged();
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex ||
            fromIndex < 0 || fromIndex >= Shortcuts.Count ||
            toIndex < 0 || toIndex >= Shortcuts.Count)
        {
            return;
        }

        Shortcuts.Move(fromIndex, toIndex);

        var model = Model.Shortcuts[fromIndex];
        Model.Shortcuts.RemoveAt(fromIndex);
        Model.Shortcuts.Insert(toIndex, model);

        Renumber();
        _onChanged();
    }

    /// <summary>Re-reads the icon and broken/ok state of one entry after it was edited.</summary>
    public void Refresh(ShortcutItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _iconCache.Evict(Model.Id, item.Id);
        item.LoadIcon(_iconCache.GetOrCreate(Model.Id, item.Model));
        item.ApplyResolution(_resolver.Resolve(item.Model));
        _onChanged();
    }

    public void NotifyShortcutCountChanged() => OnPropertyChanged(nameof(Summary));

    private ShortcutItemViewModel CreateItem(Shortcut shortcut)
    {
        var item = new ShortcutItemViewModel(shortcut);
        item.LoadIcon(_iconCache.GetOrCreate(Model.Id, shortcut));
        item.ApplyResolution(_resolver.Resolve(shortcut));
        return item;
    }

    private void Renumber()
    {
        for (var i = 0; i < Shortcuts.Count; i++)
        {
            Shortcuts[i].Ordinal = i + 1;
        }

        OnPropertyChanged(nameof(Summary));
    }
}
