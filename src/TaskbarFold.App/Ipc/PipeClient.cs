using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TaskbarFold.App.Ipc;

/// <summary>Sends a single request to the resident and reads its one-line answer.</summary>
public static class PipeClient
{
    /// <summary>
    /// Hands the resident permission to bring a window to the foreground.
    ///
    /// Windows only lets the current foreground process raise a window, or a process it
    /// has explicitly blessed. This process is the one the user just launched by clicking
    /// a pinned shortcut, so it holds that right — and the resident, which has been
    /// sitting in the tray, does not. Without this call the flyout is created and then
    /// deactivated in the same breath, and never becomes visible.
    /// </summary>
    private static void GrantForegroundRights(NamedPipeClientStream pipe)
    {
        try
        {
            var handle = pipe.SafePipeHandle.DangerousGetHandle();
            if (GetNamedPipeServerProcessId(handle, out var serverProcessId) && serverProcessId != 0)
            {
                AllowSetForegroundWindow(serverProcessId);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or EntryPointNotFoundException)
        {
            // Worst case the flyout opens without focus; not worth failing the request.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    /// <summary>
    /// Connects, writes the message, reads the reply. Returns null when no resident is
    /// listening — that is the signal to start one.
    /// </summary>
    public static string? Send(PipeMessage message, int timeoutMilliseconds = 1500)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.None,
                System.Security.Principal.TokenImpersonationLevel.Impersonation);

            pipe.Connect(timeoutMilliseconds);
            GrantForegroundRights(pipe);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(message, PipeJsonContext.Default.PipeMessage));
            return reader.ReadLine();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
