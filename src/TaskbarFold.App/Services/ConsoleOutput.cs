using System.IO;
using System.Runtime.InteropServices;

namespace TaskbarFold.App.Services;

/// <summary>
/// Writes to the console that launched this process, if there is one.
///
/// The executable is a WinExe so that launching a group never flashes a console window,
/// which means <c>--help</c> would otherwise print into the void. Attaching to the parent
/// console gets the text in front of someone running it from a terminal, and silently
/// does nothing when double-clicked.
/// </summary>
public static class ConsoleOutput
{
    private const int AttachParentProcess = -1;

    public static void Write(string text)
    {
        if (!AttachConsole(AttachParentProcess))
        {
            return;
        }

        try
        {
            Console.WriteLine();
            Console.WriteLine(text);
        }
        catch (IOException)
        {
        }
        finally
        {
            FreeConsole();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
