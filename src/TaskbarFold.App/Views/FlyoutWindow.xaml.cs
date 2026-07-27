using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarFold.App.Services;
using TaskbarFold.App.ViewModels;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;

namespace TaskbarFold.App.Views;

/// <summary>
/// The flyout itself. One instance per group is created up front and reused, so showing
/// it is a reposition plus a fade rather than a window construction.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;

    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(120);

    private readonly FlyoutViewModel _viewModel;
    private readonly bool _useAcrylic;

    /// <summary>
    /// How long after a show request a deactivation is ignored.
    ///
    /// Showing a window generates its own activation traffic, and while focus is being
    /// handed over from the signalling process Windows briefly reports the flyout as
    /// inactive. Acting on that would hide the window in the same frame it appeared.
    /// </summary>
    private static readonly TimeSpan ActivationSettleTime = TimeSpan.FromMilliseconds(300);

    private Point _dragOrigin;
    private ShortcutItemViewModel? _dragCandidate;
    private bool _isClosing;
    private DateTime _ignoreDeactivateUntil = DateTime.MinValue;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // AllowsTransparency is immutable once the handle exists, and a layered window
        // suppresses the DWM backdrop entirely — so the choice has to be made here,
        // before InitializeComponent creates anything.
        _useAcrylic = SystemBackdrop.IsSupported;
        AllowsTransparency = !_useAcrylic;

        InitializeComponent();

        DataContext = _viewModel;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
    }

    public Guid GroupId => _viewModel.Group.Id;

    public FlyoutViewModel ViewModel => _viewModel;

    /// <summary>
    /// Creates the native window without showing it. Doing this at startup moves the
    /// cost of template expansion off the click path.
    /// </summary>
    public void Prewarm()
    {
        if (new WindowInteropHelper(this).Handle != nint.Zero)
        {
            return;
        }

        // Off-screen rather than merely hidden: Show() is what forces WPF to build the
        // visual tree, and a window at these coordinates is on no monitor.
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        Show();
        Hide();
        Opacity = 1;
    }

    /// <summary>Shows the flyout against the taskbar of the monitor holding the cursor.</summary>
    public void ShowAt(int cursorX, int cursorY, Group group, AppSettings settings)
    {
        _isClosing = false;
        _ignoreDeactivateUntil = DateTime.UtcNow.Add(ActivationSettleTime);
        _viewModel.Load(group, settings);

        SearchBox.Text = string.Empty;
        SearchBox.Visibility = Visibility.Collapsed;

        var layout = TaskbarInfo.Instance.GetLayoutAt(cursorX, cursorY);

        // Measure before positioning: SizeToContent has not run yet for a window that
        // was hidden, and a zero-sized window would be placed against the wrong corner.
        Opacity = 0;
        Show();
        UpdateLayout();

        WindowPositioner.PositionAgainstTaskbar(this, layout, cursorX, cursorY);

        if (_useAcrylic)
        {
            SystemBackdrop.TryApplyAcrylic(this, dark: true);
        }
        else
        {
            SystemBackdrop.ApplyRoundedCorners(this);
        }

        ForceForeground();
        Focus();
        PlayOpenAnimation(layout.Edge);
    }

    /// <summary>
    /// Brings the flyout to the front. <see cref="Window.Activate"/> alone is unreliable
    /// here: the resident has been idle in the tray, so it only wins the foreground
    /// because the signalling process granted it that right over the pipe.
    /// </summary>
    private void ForceForeground()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            SetForegroundWindow(handle);
        }

        Activate();
    }

    private void PlayOpenAnimation(TaskbarEdge edge)
    {
        var (offsetX, offsetY) = WindowPositioner.SlideOffsetFor(edge);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        SlideTransform.X = offsetX;
        SlideTransform.Y = offsetY;

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OpenDuration));

        SlideTransform.BeginAnimation(
            TranslateTransform.XProperty, new DoubleAnimation(offsetX, 0, OpenDuration) { EasingFunction = easing });
        SlideTransform.BeginAnimation(
            TranslateTransform.YProperty, new DoubleAnimation(offsetY, 0, OpenDuration) { EasingFunction = easing });
    }

    /// <summary>Hides the window. The instance survives for the next time this group is opened.</summary>
    public void Dismiss()
    {
        if (_isClosing || !IsVisible)
        {
            return;
        }

        _isClosing = true;

        // Stop the open animation before hiding, or a queued frame can restore Opacity
        // after Hide() and leave a ghost window on screen.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
        _isClosing = false;
    }

    private void OnDeactivated(object? sender, EventArgs e) => DismissOnFocusLoss();

    private void DismissOnFocusLoss()
    {
        if (DateTime.UtcNow < _ignoreDeactivateUntil)
        {
            return;
        }

        Dismiss();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Deactivated alone misses the case where focus moves to a window on another
        // thread, which is exactly what happens when the user clicks the taskbar again.
        if (msg == WM_ACTIVATE && ((int)wParam & 0xFFFF) == WA_INACTIVE)
        {
            Dispatcher.BeginInvoke(DismissOnFocusLoss);
        }

        return nint.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    // -- Keyboard ---------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (SearchBox.Visibility == Visibility.Visible && SearchBox.Text.Length > 0)
                {
                    // First Escape clears the filter, second closes the flyout.
                    SearchBox.Text = string.Empty;
                    HideSearch();
                }
                else
                {
                    Dismiss();
                }

                e.Handled = true;
                return;

            case Key.Enter:
                _viewModel.Launch(_viewModel.SelectedItem);
                e.Handled = true;
                return;

            case Key.Left:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                return;

            case Key.Right:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                return;

            case Key.Up:
                _viewModel.MoveSelection(-_viewModel.Columns);
                e.Handled = true;
                return;

            case Key.Down:
                _viewModel.MoveSelection(_viewModel.Columns);
                e.Handled = true;
                return;

            case Key.Back when SearchBox.Visibility == Visibility.Visible && SearchBox.Text.Length <= 1:
                SearchBox.Text = string.Empty;
                HideSearch();
                e.Handled = true;
                return;
        }

        // Digits launch by position, but only while the user is not typing a search:
        // otherwise a group containing "7-Zip" becomes impossible to filter for.
        if (SearchBox.Visibility != Visibility.Visible && TryGetDigit(e.Key, out var digit))
        {
            if (_viewModel.LaunchByOrdinal(digit))
            {
                e.Handled = true;
            }
        }
    }

    private static bool TryGetDigit(Key key, out int digit)
    {
        digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };

        return digit > 0;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_viewModel.TypeToSearch)
        {
            return;
        }

        if (SearchBox.Visibility == Visibility.Visible || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (char.IsControl(character))
        {
            return;
        }

        // Type-to-search reveals the box on the first character rather than reserving
        // space for it permanently; a five-icon group should stay five icons tall.
        SearchBox.Visibility = Visibility.Visible;
        SearchBox.Text = e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        e.Handled = true;
    }

    private void HideSearch()
    {
        SearchBox.Visibility = Visibility.Collapsed;
        Focus();
    }

    // -- Mouse and drag-to-reorder ------------------------------------------------------

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as ShortcutItemViewModel;
    }

    private void OnTileClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShortcutItemViewModel item })
        {
            _viewModel.Launch(item);
        }

        _dragCandidate = null;
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragCandidate;
        _dragCandidate = null;

        // Reordering happens here rather than only in settings: walking to a settings
        // window to move one icon one place left is friction nobody accepts twice.
        DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);
    }

    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ShortcutItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnTileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ShortcutItemViewModel)) is not ShortcutItemViewModel moved ||
            sender is not FrameworkElement { DataContext: ShortcutItemViewModel target })
        {
            return;
        }

        _viewModel.Reorder(moved, _viewModel.AllItems.IndexOf(target));
        e.Handled = true;
    }

    /// <summary>Real closing only happens at shutdown; a dismissed flyout is merely hidden.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Dismiss();
            return;
        }

        base.OnClosing(e);
    }
}
