using System.Diagnostics;

namespace TaskbarFold.App.Services;

/// <summary>
/// Forces Windows Explorer to re-read the icons of already-pinned taskbar buttons.
///
/// Changing a group's icon rewrites the .ico under a new name and repoints both the
/// generated shortcut and the copy Windows made when the button was pinned. That is
/// enough for a newly pinned button, for the Start menu and for Explorer itself — but a
/// button that is *already* on the taskbar keeps its cached picture. Explorer holds
/// pinned buttons in its own taskband cache and does not reload them when the shortcut
/// changes; neither SHChangeNotify nor <c>ie4uinit -show</c> dislodges it.
///
/// Restarting the shell is the only reliable remedy, so it is offered as an explicit
/// action rather than performed behind the user's back — it closes open Explorer windows.
/// </summary>
public static class ExplorerRefresh
{
    /// <summary>
    /// Stops Explorer and lets Windows bring the shell back. Returns false when Explorer
    /// was not running or could not be stopped.
    /// </summary>
    public static bool RestartShell()
    {
        var stoppedAny = false;

        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        stoppedAny = true;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // Another session's shell, or it exited on its own.
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!stoppedAny)
        {
            return false;
        }

        // Windows normally relaunches the shell by itself. Give it a moment, then start
        // it explicitly if it has not come back, so the user is never left without a
        // desktop because of this application.
        Thread.Sleep(1200);

        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            try
            {
                using var restarted = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Error("Explorer did not come back and could not be started", ex);
                return false;
            }
        }

        Log.Info("Restarted Explorer to refresh pinned taskbar icons.");
        return true;
    }
}
