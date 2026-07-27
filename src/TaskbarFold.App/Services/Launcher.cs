using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.Services;

/// <param name="Success">False when the application did not start.</param>
/// <param name="Message">Plain-language reason, ready to show in the flyout.</param>
public sealed record LaunchResult(bool Success, string? Message = null)
{
    public static LaunchResult Ok { get; } = new(true);
}

/// <summary>Starts the application behind a shortcut, including packaged ones.</summary>
public sealed class Launcher
{
    private readonly ShortcutResolver _resolver;

    public Launcher(ShortcutResolver? resolver = null) => _resolver = resolver ?? new ShortcutResolver();

    public LaunchResult Launch(Shortcut shortcut, bool forceAsAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        var resolution = _resolver.Resolve(shortcut);
        if (!resolution.CanLaunch)
        {
            return new LaunchResult(false, resolution.Message ?? "This shortcut cannot be opened.");
        }

        if (resolution.State == ShortcutState.Relocated)
        {
            Log.Info($"'{shortcut.DisplayName}' was found at its new location: {resolution.ResolvedTarget}");
        }

        try
        {
            var startInfo = shortcut.Kind == ShortcutKind.Packaged
                ? BuildPackagedStart(shortcut)
                : BuildDesktopStart(shortcut, resolution.ResolvedTarget!, forceAsAdmin);

            using var process = Process.Start(startInfo);
            return LaunchResult.Ok;
        }
        catch (Win32Exception ex)
        {
            // 1223 is ERROR_CANCELLED: the user dismissed the elevation prompt. That is
            // a decision, not a failure, and does not deserve an error message.
            if (ex.NativeErrorCode == 1223)
            {
                return LaunchResult.Ok;
            }

            Log.Error($"Failed to launch '{shortcut.DisplayName}'", ex);
            return new LaunchResult(false, Describe(shortcut, ex));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            Log.Error($"Failed to launch '{shortcut.DisplayName}'", ex);
            return new LaunchResult(false, Describe(shortcut, ex));
        }
    }

    /// <summary>
    /// Opens the Windows file properties dialog for a path.
    ///
    /// This needs ShellExecuteEx with SEE_MASK_INVOKEIDLIST rather than Process.Start:
    /// the "properties" verb is supplied by the shell's context menu handler, not by the
    /// file association, so it is not reachable through a plain shell execute.
    /// </summary>
    public static void ShowFileProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
        };

        if (!ShellExecuteEx(ref info))
        {
            Log.Warn($"Could not open the properties dialog for '{path}'.");
        }
    }

    /// <summary>Opens the containing folder with the file selected.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add($"/select,{path}");
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not reveal '{path}' in Explorer", ex);
        }
    }

    private static ProcessStartInfo BuildDesktopStart(Shortcut shortcut, string target, bool forceAsAdmin)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            // The shell verb is what makes documents, .lnk files and installers behave
            // the way a double-click in Explorer would.
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(shortcut, target),
        };

        if (!string.IsNullOrWhiteSpace(shortcut.Arguments))
        {
            startInfo.Arguments = shortcut.Arguments;
        }

        if (shortcut.RunAsAdmin || forceAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    /// <summary>
    /// Packaged apps have no executable to run. The activation path is the shell's
    /// AppsFolder entry for the AUMID, which Explorer resolves to the real activation.
    /// </summary>
    private static ProcessStartInfo BuildPackagedStart(Shortcut shortcut)
    {
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(IconExtractor.AppsFolderPrefix + shortcut.AppUserModelId);
        return startInfo;
    }

    private static string ResolveWorkingDirectory(Shortcut shortcut, string target)
    {
        if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory) &&
            Directory.Exists(shortcut.WorkingDirectory))
        {
            return shortcut.WorkingDirectory;
        }

        return Path.GetDirectoryName(target) ?? Environment.CurrentDirectory;
    }

    /// <summary>
    /// Turns an exception into something worth reading. Showing the raw exception text
    /// in a message box is one of the things this application exists to stop doing.
    /// </summary>
    private static string Describe(Shortcut shortcut, Exception exception)
    {
        var name = string.IsNullOrWhiteSpace(shortcut.DisplayName) ? "This application" : shortcut.DisplayName;

        return exception switch
        {
            Win32Exception { NativeErrorCode: 2 } => $"{name} is no longer installed at that location.",
            Win32Exception { NativeErrorCode: 5 } => $"Windows refused to start {name}. It may need administrator rights.",
            Win32Exception { NativeErrorCode: 1155 } =>
                $"Windows has no application registered to open {name}.",
            FileNotFoundException => $"{name} is no longer installed at that location.",
            _ => $"{name} could not be started.",
        };
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIcon;
        public nint hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
