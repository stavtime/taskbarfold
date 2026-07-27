using System.Globalization;

namespace TaskbarFold.App;

/// <param name="GroupId">Set by <c>--group</c>: signal the resident and exit without any UI.</param>
/// <param name="StartHidden">Set by <c>--tray</c>: come up in the tray without opening settings.</param>
/// <param name="ShowHelp">Set by <c>--help</c>.</param>
/// <param name="Error">Message describing a malformed command line, if any.</param>
public sealed record CommandLineOptions(
    Guid? GroupId = null,
    bool StartHidden = false,
    bool ShowHelp = false,
    string? Error = null)
{
    public const string Usage = """
        TaskbarFold — taskbar mini-folders for Windows 10 and 11.

          TaskbarFold.exe                  Open settings; the resident stays in the tray.
          TaskbarFold.exe --group <GUID>   Ask the resident to show that group's flyout.
          TaskbarFold.exe --tray           Start the resident without opening settings.
          TaskbarFold.exe --help           Show this text.

        The --group form is what a pinned shortcut runs. It creates no window: it sends the
        group id and the cursor position over a named pipe and exits immediately.
        """;

    public static CommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Guid? groupId = null;
        var startHidden = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--group" or "-g" or "/group":
                    if (i + 1 >= args.Length)
                    {
                        return new CommandLineOptions(Error: "--group requires a group id.");
                    }

                    if (!Guid.TryParse(args[++i], CultureInfo.InvariantCulture, out var parsed))
                    {
                        return new CommandLineOptions(Error: $"'{args[i]}' is not a valid group id.");
                    }

                    groupId = parsed;
                    break;

                case "--tray" or "-t" or "/tray":
                    startHidden = true;
                    break;

                case "--help" or "-h" or "-?" or "/?":
                    return new CommandLineOptions(ShowHelp: true);

                default:
                    return new CommandLineOptions(Error: $"Unrecognised argument '{arg}'.");
            }
        }

        return new CommandLineOptions(groupId, startHidden);
    }
}
