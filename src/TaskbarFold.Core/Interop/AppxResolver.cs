using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace TaskbarFold.Core.Interop;

/// <param name="DisplayName">Name as shown in the Start menu.</param>
/// <param name="AppUserModelId">Launch identity, used with <c>shell:AppsFolder\</c>.</param>
/// <param name="IsPackaged">True for Store/UWP entries, false for classic desktop apps.</param>
public sealed record InstalledApp(string DisplayName, string AppUserModelId, bool IsPackaged);

public interface IInstalledAppProvider
{
    IReadOnlyList<InstalledApp> GetInstalledApps(CancellationToken cancellationToken = default);
}

/// <summary>
/// Enumerates everything launchable through <c>shell:AppsFolder</c> and reads each
/// entry's AppUserModelID.
///
/// This is the only approach that covers Store apps without elevation:
/// <c>%ProgramFiles%\WindowsApps</c> is not readable by a normal user, and packaged
/// apps expose no executable path at all — just an AUMID.
/// </summary>
public sealed class AppxResolver : IInstalledAppProvider
{
    private const string AppsFolderParsingName = "shell:AppsFolder";

    /// <summary>An AUMID of a packaged app has the form <c>PackageFamilyName!AppId</c>.</summary>
    public static bool IsPackagedAppUserModelId(string appUserModelId) =>
        !string.IsNullOrWhiteSpace(appUserModelId) && appUserModelId.Contains('!', StringComparison.Ordinal);

    public IReadOnlyList<InstalledApp> GetInstalledApps(CancellationToken cancellationToken = default)
    {
        var results = new List<InstalledApp>();

        object? folderObject = null;
        IEnumShellItems? enumerator = null;
        try
        {
            NativeMethods.SHCreateItemFromParsingName(
                AppsFolderParsingName, 0, ShellGuids.IShellItem, out folderObject);

            if (folderObject is not IShellItem folder)
            {
                return results;
            }

            folder.BindToHandler(0, ShellGuids.BHID_EnumItems, typeof(IEnumShellItems).GUID, out var raw);
            enumerator = raw as IEnumShellItems;
            if (enumerator is null)
            {
                return results;
            }

            var batch = new IShellItem[1];
            while (enumerator.Next(1, batch, out var fetched) == 0 && fetched == 1)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = batch[0];
                try
                {
                    var app = Describe(item);
                    if (app is not null)
                    {
                        results.Add(app);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (COMException)
        {
            // The Apps folder is unavailable (a locked-down or broken shell); the caller
            // still has the "browse for a file" path, so an empty list is an acceptable answer.
        }
        finally
        {
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }

            if (folderObject is not null)
            {
                Marshal.ReleaseComObject(folderObject);
            }
        }

        return results
            .GroupBy(a => a.AppUserModelId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static InstalledApp? Describe(IShellItem item)
    {
        string displayName;
        try
        {
            item.GetDisplayName(SIGDN.NORMALDISPLAY, out displayName);
        }
        catch (COMException)
        {
            return null;
        }

        if (item is not IShellItem2 item2)
        {
            return null;
        }

        var key = ShellGuids.PKEY_AppUserModel_ID;
        if (item2.GetString(in key, out var aumid) != 0 || string.IsNullOrWhiteSpace(aumid))
        {
            // Entries without an AUMID are shell verbs and control panel items, not apps.
            return null;
        }

        return new InstalledApp(displayName, aumid, IsPackagedAppUserModelId(aumid));
    }

    /// <summary>
    /// Reads display name and logo out of an <c>AppxManifest.xml</c>. Only usable when
    /// the package folder happens to be readable; the Apps folder enumeration above is
    /// the path that always works.
    /// </summary>
    public static AppxManifestInfo? ReadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(manifestPath);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var ns = root.Name.Namespace;

            var identity = root.Element(ns + "Identity");
            var packageName = identity?.Attribute("Name")?.Value;

            var properties = root.Element(ns + "Properties");
            var displayName = properties?.Element(ns + "DisplayName")?.Value;
            var logo = properties?.Element(ns + "Logo")?.Value;

            var application = root.Element(ns + "Applications")?.Elements().FirstOrDefault();
            var applicationId = application?.Attribute("Id")?.Value;

            if (packageName is null || applicationId is null)
            {
                return null;
            }

            var packageFolder = Path.GetDirectoryName(manifestPath);
            var logoPath = logo is null || packageFolder is null
                ? null
                : Path.Combine(packageFolder, logo.Replace('/', Path.DirectorySeparatorChar));

            return new AppxManifestInfo(packageName, applicationId, displayName ?? packageName, logoPath);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return null;
        }
    }
}

/// <param name="PackageName">Identity/Name from the manifest.</param>
/// <param name="ApplicationId">Applications/Application@Id.</param>
/// <param name="DisplayName">Human-readable name; may still be an <c>ms-resource:</c> reference.</param>
/// <param name="LogoPath">Absolute path of the declared logo, when the folder is readable.</param>
public sealed record AppxManifestInfo(
    string PackageName,
    string ApplicationId,
    string DisplayName,
    string? LogoPath);
