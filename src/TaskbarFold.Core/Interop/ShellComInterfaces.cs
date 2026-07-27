using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TaskbarFold.Core.Interop;

internal static class ShellGuids
{
    internal static readonly Guid IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    internal static readonly Guid IShellItem2 = new("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
    internal static readonly Guid IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    /// <summary>BHID_EnumItems — binds a folder shell item to an enumerator of its children.</summary>
    internal static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    /// <summary>PKEY_AppUserModel_ID, the AUMID both packaged apps and pinned shortcuts carry.</summary>
    internal static readonly PROPERTYKEY PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
}

internal enum SIGDN : uint
{
    NORMALDISPLAY = 0x00000000,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    FILESYSPATH = 0x80058000,
}

[Flags]
internal enum SIIGBF
{
    ResizeToFit = 0x00,
    BiggerSizeOk = 0x01,
    MemoryOnly = 0x02,
    IconOnly = 0x04,
    ThumbnailOnly = 0x08,
    InCacheOnly = 0x10,
    ScaleUp = 0x100,
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetParent(out IShellItem ppsi);

    void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    void Compare(IShellItem psi, uint hint, out int piOrder);
}

/// <summary>
/// Declared flat rather than deriving from <see cref="IShellItem"/>: a ComImport
/// interface that inherits another one would lay out the base slots twice.
/// </summary>
[ComImport]
[Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetParent(out IShellItem ppsi);

    void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    void Compare(IShellItem psi, uint hint, out int piOrder);

    void GetPropertyStore(uint flags, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetPropertyStoreWithCreateObject(uint flags, [MarshalAs(UnmanagedType.IUnknown)] object punkCreateObject, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetPropertyStoreForKeys(nint rgKeys, uint cKeys, uint flags, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetPropertyDescriptionList(in PROPERTYKEY keyType, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void Update(nint pbc);

    void GetProperty(in PROPERTYKEY key, nint ppropvar);

    void GetCLSID(in PROPERTYKEY key, out Guid pclsid);

    void GetFileTime(in PROPERTYKEY key, out FILETIME pft);

    void GetInt32(in PROPERTYKEY key, out int pi);

    [PreserveSig]
    int GetString(in PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);

    void GetUInt32(in PROPERTYKEY key, out uint pui);

    void GetUInt64(in PROPERTYKEY key, out ulong pull);

    void GetBool(in PROPERTYKEY key, [MarshalAs(UnmanagedType.Bool)] out bool pf);
}

[ComImport]
[Guid("70629033-E363-4A28-A567-0DB78006E6D7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    void Reset();

    void Clone(out IEnumShellItems ppenum);
}

/// <summary>
/// The one icon API that works uniformly for files, folders and packaged apps, and
/// that returns the highest-resolution asset available rather than a 32px stamp.
/// </summary>
[ComImport]
[Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, SIIGBF flags, out nint phbm);
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);

    void GetIDList(out nint ppidl);

    void SetIDList(nint pidl);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out ushort pwHotkey);

    void SetHotkey(ushort wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    void Resolve(nint hwnd, uint fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);

    void GetAt(uint iProp, out PROPERTYKEY pkey);

    void GetValue(in PROPERTYKEY key, nint pv);

    void SetValue(in PROPERTYKEY key, nint pv);

    void Commit();
}

/// <summary>CLSID_ShellLink. Instantiating this type performs the CoCreateInstance.</summary>
[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass
{
}
