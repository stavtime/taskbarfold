using Microsoft.Win32;

namespace TaskbarFold.App.Services;

/// <summary>
/// Toggles "start with Windows" through <c>HKCU\...\Run</c>.
///
/// A scheduled task would allow a delayed start, but registering one needs elevation.
/// The Run key is per-user and writable without any prompt, which matters for a utility
/// nobody expects to hand administrator rights to.
/// </summary>
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarFold";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            // --tray, so signing in does not throw the settings window at the user.
            key.SetValue(ValueName, $"\"{executable}\" --tray", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Log.Error("Could not update the auto-start entry", ex);
            return false;
        }
    }
}
