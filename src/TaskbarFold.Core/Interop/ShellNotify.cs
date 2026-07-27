using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

/// <summary>
/// Tells Explorer that something it has cached changed.
///
/// Rewriting a .ico is not enough on its own: the shell keeps a bitmap cache keyed by
/// icon path, and a pinned taskbar button will happily keep drawing the old picture
/// until it is told otherwise.
/// </summary>
public static class ShellNotify
{
    private const uint SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;

    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_FLUSH = 0x1000;

    /// <summary>Announces that a single file's contents changed.</summary>
    public static void FileUpdated(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var native = Marshal.StringToHGlobalUni(path);
        try
        {
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, native, nint.Zero);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                var nativeDirectory = Marshal.StringToHGlobalUni(directory);
                try
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, nativeDirectory, nint.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(nativeDirectory);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    /// <summary>
    /// The broad "icons may have changed" notification. Heavier than
    /// <see cref="FileUpdated"/> and used only after an icon a pinned shortcut points at
    /// has actually been rewritten.
    /// </summary>
    public static void IconsChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, nint.Zero, nint.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
