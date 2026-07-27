using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskbarFold.App.Services;

/// <summary>
/// Registers system-wide hotkeys that open a specific group.
///
/// Messages arrive on a message-only window rather than a real one, so the hotkeys keep
/// working while every flyout is hidden — which is the normal state of this application.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, Guid> _registrations = [];
    private readonly HwndSource _source;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("TaskbarFold.Hotkeys")
        {
            // HWND_MESSAGE: no pixels, no taskbar presence, still receives thread messages.
            ParentWindow = new nint(-3),
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Raised on the UI thread when a registered combination is pressed.</summary>
    public event EventHandler<Guid>? HotkeyPressed;

    /// <summary>
    /// Replaces all registrations with the given set. Returns the groups whose hotkey
    /// could not be claimed, usually because another application already owns it.
    /// </summary>
    public IReadOnlyList<Guid> Register(IEnumerable<(Guid GroupId, string Gesture)> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        UnregisterAll();
        var failed = new List<Guid>();

        foreach (var (groupId, gesture) in hotkeys)
        {
            if (!TryParse(gesture, out var modifiers, out var virtualKey))
            {
                failed.Add(groupId);
                continue;
            }

            var id = _nextId++;

            // MOD_NOREPEAT stops a held-down combination from firing continuously.
            if (RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, virtualKey))
            {
                _registrations[id] = groupId;
            }
            else
            {
                failed.Add(groupId);
                Log.Warn($"Hotkey '{gesture}' is already taken by another application.");
            }
        }

        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registrations.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registrations.Clear();
    }

    /// <summary>Parses gestures such as <c>Ctrl+Alt+D1</c> or <c>Win+Shift+W</c>.</summary>
    public static bool TryParse(string? gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        // A bare key with no modifier would swallow that key for the whole session.
        if (modifiers == 0)
        {
            return false;
        }

        if (!Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key))
        {
            return false;
        }

        var code = KeyInterop.VirtualKeyFromKey(key);
        if (code == 0)
        {
            return false;
        }

        virtualKey = (uint)code;
        return true;
    }

    /// <summary>Formats a WPF key combination the way <see cref="TryParse"/> reads it.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (modifiers == ModifierKeys.None || key is Key.None or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System)
        {
            return null;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());

        return string.Join('+', parts);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY)
        {
            return nint.Zero;
        }

        var id = (int)wParam;
        if (_registrations.TryGetValue(id, out var groupId))
        {
            handled = true;
            HotkeyPressed?.Invoke(this, groupId);
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _disposed = true;
    }

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
