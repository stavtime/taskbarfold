using System.Diagnostics;
using System.IO;
using TaskbarFold.App.Ipc;
using TaskbarFold.App.Services;
using TaskbarFold.Core.Interop;

namespace TaskbarFold.App;

public static class Program
{
    /// <summary>Exit code used when the resident could not be reached or started.</summary>
    private const int ExitSignalFailed = 2;

    private const int ExitBadArguments = 64;

    [STAThread]
    public static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        if (options.Error is not null)
        {
            ConsoleOutput.Write($"{options.Error}{Environment.NewLine}{Environment.NewLine}{CommandLineOptions.Usage}");
            return ExitBadArguments;
        }

        if (options.ShowHelp)
        {
            ConsoleOutput.Write(CommandLineOptions.Usage);
            return 0;
        }

        // The signalling mode deliberately never touches WPF. Building an Application
        // here would cost tens of milliseconds and load a UI stack this process has no
        // use for — the whole point is that the click goes to an already-warm resident.
        return options.GroupId is { } groupId ? RunSignalMode(groupId) : RunResidentMode(options);
    }

    private static int RunSignalMode(Guid groupId)
    {
        var (x, y) = TaskbarInfo.Instance.GetCursorPosition();
        var message = new PipeMessage(PipeProtocol.ShowGroup, groupId, x, y);

        // Connecting to a pipe nobody created does not fail fast: it polls until the timeout.
        // With no resident there is nothing to wait for, so the click goes straight to
        // starting one instead of sitting through a second and a half of that first.
        if (SingleInstance.IsRunning())
        {
            var reply = PipeClient.Send(message);
            if (reply is not null)
            {
                return reply == PipeProtocol.ResponseOk ? 0 : ExitSignalFailed;
            }
        }

        // No resident listening. Start one and try again, exactly once: retrying in a
        // loop would spawn a pile of processes if startup is what is actually broken.
        if (!TryStartResident())
        {
            return ExitSignalFailed;
        }

        var response = PipeClient.Send(message, timeoutMilliseconds: 8000);
        return response == PipeProtocol.ResponseOk ? 0 : ExitSignalFailed;
    }

    private static bool TryStartResident()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            };

            // --tray, so the resident comes up silently instead of opening settings
            // in the user's face just because they clicked a pinned group.
            startInfo.ArgumentList.Add("--tray");

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static int RunResidentMode(CommandLineOptions options)
    {
        using var instance = SingleInstance.Acquire();

        if (!instance.IsOwner)
        {
            // Someone launched the executable while the resident is already up; the
            // useful interpretation is "show me the settings", so forward that and quit.
            if (!options.StartHidden)
            {
                PipeClient.Send(new PipeMessage(PipeProtocol.OpenSettings));
            }

            return 0;
        }

        var app = new App(options);
        app.InitializeComponent();
        return app.Run();
    }
}
