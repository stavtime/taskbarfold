using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace TaskbarFold.Core.Interop;

/// <param name="TargetPath">Executable the link points at.</param>
/// <param name="Arguments">Command line passed to the target.</param>
/// <param name="WorkingDirectory">Start-in folder.</param>
/// <param name="Description">Tooltip text.</param>
/// <param name="IconPath">Path of the .ico supplying the tile.</param>
/// <param name="AppUserModelId">
/// Written to <c>System.AppUserModel.ID</c>. Without a distinct value per group
/// Windows merges every pinned group into one taskbar button.
/// </param>
public sealed record ShellLinkData(
    string TargetPath,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? Description = null,
    string? IconPath = null,
    string? AppUserModelId = null);

public interface IShellLinkWriter
{
    void CreateOrUpdate(string linkPath, ShellLinkData data);
}

/// <summary>
/// Creates and edits .lnk files.
///
/// Edits load the existing file and save it back in place. Deleting and recreating the
/// .lnk would be simpler but it drops the taskbar pin, because Windows tracks the pin
/// by the file's identity rather than its path.
/// </summary>
public sealed class ShellLink : IShellLinkWriter
{
    /// <summary>Size of a PROPVARIANT on 64-bit Windows, with headroom.</summary>
    private const int PropVariantSize = 32;

    /// <summary>STGM_READ — enough to read a link back.</summary>
    private const int StgmRead = 0x00000000;

    /// <summary>
    /// STGM_READWRITE. Loading an existing link read-only yields a read-only property
    /// store, and writing the AppUserModelID then fails with STG_E_ACCESSDENIED.
    /// </summary>
    private const int StgmReadWrite = 0x00000002;

    public static ShellLink Instance { get; } = new();

    public void CreateOrUpdate(string linkPath, ShellLinkData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentNullException.ThrowIfNull(data);

        var directory = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        object? comObject = null;
        try
        {
            comObject = new ShellLinkCoClass();
            var link = (IShellLinkW)comObject;
            var persist = (IPersistFile)comObject;

            if (File.Exists(linkPath))
            {
                // Load first so unrelated properties written by the shell survive the edit.
                persist.Load(linkPath, StgmReadWrite);
            }

            link.SetPath(data.TargetPath);
            link.SetArguments(data.Arguments ?? string.Empty);
            link.SetWorkingDirectory(
                data.WorkingDirectory ?? Path.GetDirectoryName(data.TargetPath) ?? string.Empty);
            link.SetDescription(Truncate(data.Description ?? string.Empty, 259));

            if (!string.IsNullOrWhiteSpace(data.IconPath))
            {
                link.SetIconLocation(data.IconPath, 0);
            }

            if (!string.IsNullOrWhiteSpace(data.AppUserModelId))
            {
                SetAppUserModelId(comObject, data.AppUserModelId);
            }

            persist.Save(linkPath, true);
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    /// <summary>Reads back the AUMID of an existing link, or null when it carries none.</summary>
    public string? ReadAppUserModelId(string linkPath)
    {
        if (!File.Exists(linkPath))
        {
            return null;
        }

        object? comObject = null;
        var buffer = nint.Zero;
        try
        {
            comObject = new ShellLinkCoClass();
            ((IPersistFile)comObject).Load(linkPath, StgmRead);

            var store = (IPropertyStore)comObject;
            buffer = Marshal.AllocCoTaskMem(PropVariantSize);
            InitializeZeroed(buffer);

            var key = ShellGuids.PKEY_AppUserModel_ID;
            store.GetValue(in key, buffer);

            if (Marshal.ReadInt16(buffer) != NativeMethods.VT_LPWSTR)
            {
                return null;
            }

            var stringPointer = Marshal.ReadIntPtr(buffer, NativeMethods.PropVariantValueOffset);
            return stringPointer == nint.Zero ? null : Marshal.PtrToStringUni(stringPointer);
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            return null;
        }
        finally
        {
            if (buffer != nint.Zero)
            {
                NativeMethods.PropVariantClear(buffer);
                Marshal.FreeCoTaskMem(buffer);
            }

            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    /// <summary>
    /// Builds a VT_LPWSTR PROPVARIANT by hand and stores it under
    /// <c>System.AppUserModel.ID</c>.
    ///
    /// The string is allocated with the COM task allocator because
    /// <c>PropVariantClear</c> is what frees it — which is also why the pointer is not
    /// released separately in the finally block.
    /// </summary>
    /// <summary>Reads the icon path a shortcut points at, or null when it has none.</summary>
    public string? ReadIconLocation(string linkPath)
    {
        if (!File.Exists(linkPath))
        {
            return null;
        }

        object? comObject = null;
        try
        {
            comObject = new ShellLinkCoClass();
            ((IPersistFile)comObject).Load(linkPath, StgmRead);

            var buffer = new System.Text.StringBuilder(1024);
            ((IShellLinkW)comObject).GetIconLocation(buffer, buffer.Capacity, out _);

            var value = buffer.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            return null;
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    /// <summary>
    /// Changes only the icon of an existing shortcut, leaving everything else — including
    /// the AppUserModelID that keeps the taskbar pin distinct — exactly as it was.
    /// </summary>
    public void SetIconLocation(string linkPath, string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);

        object? comObject = null;
        try
        {
            comObject = new ShellLinkCoClass();
            var persist = (IPersistFile)comObject;

            persist.Load(linkPath, StgmReadWrite);
            ((IShellLinkW)comObject).SetIconLocation(iconPath, 0);
            persist.Save(linkPath, true);
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    private static void SetAppUserModelId(object comObject, string appUserModelId)
    {
        var store = (IPropertyStore)comObject;
        var buffer = Marshal.AllocCoTaskMem(PropVariantSize);
        try
        {
            InitializeZeroed(buffer);

            var value = Marshal.StringToCoTaskMemUni(appUserModelId);
            Marshal.WriteInt16(buffer, 0, NativeMethods.VT_LPWSTR);
            Marshal.WriteIntPtr(buffer, NativeMethods.PropVariantValueOffset, value);

            var key = ShellGuids.PKEY_AppUserModel_ID;
            store.SetValue(in key, buffer);
            store.Commit();
        }
        finally
        {
            NativeMethods.PropVariantClear(buffer);
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static void InitializeZeroed(nint buffer)
    {
        for (var i = 0; i < PropVariantSize; i++)
        {
            Marshal.WriteByte(buffer, i, 0);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
