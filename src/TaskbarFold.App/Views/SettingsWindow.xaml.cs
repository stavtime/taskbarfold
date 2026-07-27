using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TaskbarFold.App.Services;
using TaskbarFold.App.ViewModels;
using TaskbarFold.Core.Interop;

namespace TaskbarFold.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    private Point _dragOrigin;
    private ShortcutItemViewModel? _dragCandidate;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            SystemBackdrop.SetDarkMode(this, ThemeService.IsSystemDark());

            // Enumerating installed applications takes a moment; start it as soon as the
            // window is up rather than making the user wait after they click Add.
            await _viewModel.LoadInstalledAppsCommand.ExecuteAsync(null).ConfigureAwait(true);
        };
    }

    // -- Group icon ---------------------------------------------------------------------

    private void OnChooseGroupIcon(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a group icon",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SelectedGroup.IconPath = dialog.FileName;
            _viewModel.StatusMessage = "Icon updated. Re-pin the group if it is already on the taskbar.";
        }
    }

    private void OnClearGroupIcon(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        // Reset drops both overrides, leaving the tile on the group's colour and initial.
        _viewModel.SelectedGroup.IconPath = null;
        _viewModel.SelectedGroup.Emoji = null;
    }

    private void OnEmojiPicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string emoji } || _viewModel.SelectedGroup is null)
        {
            return;
        }

        // A picked emoji supersedes a custom image, otherwise the tile would not change
        // and the click would look like it did nothing.
        _viewModel.SelectedGroup.IconPath = null;
        _viewModel.SelectedGroup.Emoji = emoji;
        _viewModel.StatusMessage = "Icon updated. Re-pin the group if it is already on the taskbar.";
    }

    private void OnColorPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string color } && _viewModel.SelectedGroup is not null)
        {
            _viewModel.SelectedGroup.Background = color;
        }
    }

    // -- Hotkey capture -------------------------------------------------------------------

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (_viewModel.SelectedGroup is null)
        {
            return;
        }

        // AltGr and IME input arrive as Key.System; the real key is in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = HotkeyService.Format(Keyboard.Modifiers, key);

        if (gesture is null)
        {
            _viewModel.StatusMessage = "A hotkey needs at least one of Ctrl, Alt, Shift or Win.";
            return;
        }

        _viewModel.SelectedGroup.Hotkey = gesture;
        _viewModel.StatusMessage = $"Hotkey set to {gesture}.";
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGroup is not null)
        {
            _viewModel.SelectedGroup.Hotkey = null;
        }
    }

    // -- Adding shortcuts -------------------------------------------------------------------

    private void OnAddFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add shortcuts",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddPaths(dialog.FileNames);
        }
    }

    private void OnInstalledAppActivated(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: InstalledApp app })
        {
            _viewModel.AddInstalledApp(app);
        }
    }

    private void OnShortcutFieldChanged(object sender, RoutedEventArgs e) =>
        _viewModel.RefreshSelectedShortcut();

    // -- Drag and drop -----------------------------------------------------------------------

    private void OnShortcutsDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(ShortcutItemViewModel)))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnShortcutsDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddPaths(paths.Where(p => File.Exists(p) || Directory.Exists(p)));
            e.Handled = true;
        }
    }

    private void OnShortcutMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _dragCandidate = (e.OriginalSource as FrameworkElement)?.DataContext as ShortcutItemViewModel;
    }

    private void OnShortcutMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(ShortcutList, dragged, DragDropEffects.Move);
    }

    private void OnShortcutReorderDrop(object sender, DragEventArgs e)
    {
        if (_viewModel.SelectedGroup is null ||
            e.Data.GetData(typeof(ShortcutItemViewModel)) is not ShortcutItemViewModel moved)
        {
            return;
        }

        var target = (e.OriginalSource as FrameworkElement)?.DataContext as ShortcutItemViewModel;
        var group = _viewModel.SelectedGroup;

        var from = group.Shortcuts.IndexOf(moved);
        var to = target is null ? group.Shortcuts.Count - 1 : group.Shortcuts.IndexOf(target);

        group.Move(from, to);
        e.Handled = true;
    }

    /// <summary>
    /// Closing the settings window leaves the resident running, which is the whole point
    /// of the tray icon; the process only exits from the tray menu.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
