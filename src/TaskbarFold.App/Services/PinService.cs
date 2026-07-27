using System.Diagnostics;
using System.IO;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;

namespace TaskbarFold.App.Services;

/// <param name="Success">Whether the .lnk is ready for the user to pin.</param>
/// <param name="LinkPath">Path of the generated shortcut.</param>
/// <param name="Message">What to tell the user.</param>
public sealed record PinPreparation(bool Success, string? LinkPath, string Message);

/// <summary>
/// Produces the .lnk a user pins to the taskbar, and opens Explorer with it selected.
///
/// Pinning cannot be automated. Windows 10 and 11 deliberately removed the API, and the
/// registry and LayoutModification.xml workarounds break with each feature update. So the
/// goal here is not to fake it but to reduce it to one click plus one drag, with the file
/// already highlighted.
/// </summary>
public sealed class PinService
{
    private readonly AppPaths _paths;
    private readonly IShellLinkWriter _linkWriter;

    public PinService(AppPaths? paths = null, IShellLinkWriter? linkWriter = null)
    {
        _paths = paths ?? AppPaths.Default;
        _linkWriter = linkWriter ?? ShellLink.Instance;
    }

    /// <summary>
    /// Creates or refreshes the group's .lnk and its icon.
    ///
    /// The file is edited in place, never deleted and recreated: Windows tracks a taskbar
    /// pin by the shortcut's identity, so recreating the file silently unpins the group.
    /// </summary>
    public PinPreparation PrepareLink(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return new PinPreparation(false, null, "TaskbarFold could not determine its own location.");
        }

        try
        {
            _paths.EnsureCreated();

            var iconPath = EnsureIcon(group);
            var linkPath = Path.Combine(_paths.LinksRoot, GroupIdentity.LinkFileNameFor(group));

            // A group renamed since the last pin keeps its old .lnk, because that file is
            // what the taskbar points at. Editing it preserves the pin; the display name
            // on the taskbar follows the file name, so it is refreshed on the next pin.
            var existing = FindExistingLink(group);
            if (existing is not null)
            {
                linkPath = existing;
            }

            _linkWriter.CreateOrUpdate(linkPath, new ShellLinkData(
                TargetPath: executable,
                Arguments: $"--group {group.Id}",
                WorkingDirectory: Path.GetDirectoryName(executable),
                Description: $"TaskbarFold group: {group.Name}",
                IconPath: iconPath,
                AppUserModelId: group.AppUserModelId));

            return new PinPreparation(
                true,
                linkPath,
                "Drag the highlighted shortcut onto the taskbar, or right-click it and choose “Pin to taskbar”.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Error($"Could not prepare the pin shortcut for '{group.Name}'", ex);
            return new PinPreparation(false, null, "The shortcut file could not be created.");
        }
    }

    /// <summary>Opens Explorer at the shortcut with it already selected.</summary>
    public static void RevealLink(string linkPath) => Launcher.RevealInExplorer(linkPath);

    /// <summary>
    /// Writes the group's icon and returns its path.
    ///
    /// The file name carries a hash of the contents, e.g. <c>{guid}-1f3a9c0b.ico</c>.
    /// That is deliberate: Explorer caches taskbar icons by path, and rewriting the same
    /// path leaves a pinned button showing the old picture no matter what change
    /// notification is sent. A new path is the only reliable way to retire the cached
    /// bitmap. Stale versions are deleted once the shortcuts point at the new one.
    /// </summary>
    public string EnsureIcon(Group group)
    {
        // A user-supplied .ico is used as-is; anything else is rebuilt from the group.
        if (!string.IsNullOrWhiteSpace(group.IconPath) &&
            File.Exists(group.IconPath) &&
            string.Equals(Path.GetExtension(group.IconPath), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            return group.IconPath;
        }

        var bytes = BuildIconBytes(group);
        if (bytes is null)
        {
            return _paths.IconFileFor(group.Id);
        }

        var iconPath = VersionedIconPath(group.Id, bytes);
        WriteIfMissing(iconPath, bytes);
        return iconPath;
    }

    /// <summary>
    /// Rebuilds the group's icon when its appearance changed and repoints every shortcut
    /// that uses it, including the copy Windows made when the user pinned it to the
    /// taskbar. Returns true when anything changed.
    /// </summary>
    public bool RefreshIcon(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!string.IsNullOrWhiteSpace(group.IconPath) &&
            File.Exists(group.IconPath) &&
            string.Equals(Path.GetExtension(group.IconPath), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var bytes = BuildIconBytes(group);
        if (bytes is null)
        {
            return false;
        }

        var iconPath = VersionedIconPath(group.Id, bytes);
        var links = FindLinksFor(group).ToList();

        // Nothing to do when the icon already exists under this name and every shortcut
        // already points at it.
        if (File.Exists(iconPath) && links.All(l => PointsAt(l, iconPath)))
        {
            return false;
        }

        WriteIfMissing(iconPath, bytes);

        foreach (var link in links)
        {
            RepointLink(link, iconPath);
            ShellNotify.FileUpdated(link);
        }

        PruneOldIcons(group.Id, iconPath);
        ShellNotify.FileUpdated(iconPath);
        ShellNotify.IconsChanged();

        Log.Info($"Refreshed the icon for '{group.Name}' -> {Path.GetFileName(iconPath)} ({links.Count} shortcut(s)).");
        return true;
    }

    /// <summary>
    /// Whether this group already has a generated icon. Matches on the id prefix, because
    /// the file name also carries a content hash.
    /// </summary>
    public bool HasGeneratedIcon(Guid groupId)
    {
        try
        {
            return Directory.Exists(_paths.IconsRoot) &&
                   Directory.EnumerateFiles(_paths.IconsRoot, $"{groupId:N}*.ico").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Icon path for a given payload: the group id plus a short content hash.</summary>
    private string VersionedIconPath(Guid groupId, byte[] bytes)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..8].ToLowerInvariant();
        return Path.Combine(_paths.IconsRoot, $"{groupId:N}-{hash}.ico");
    }

    private static bool PointsAt(string linkPath, string iconPath)
    {
        var current = ShellLink.Instance.ReadIconLocation(linkPath);
        return string.Equals(current, iconPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void RepointLink(string linkPath, string iconPath)
    {
        try
        {
            ShellLink.Instance.SetIconLocation(linkPath, iconPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Could not update the icon of '{linkPath}': {ex.Message}");
        }
    }

    /// <summary>Removes superseded icon files for a group, keeping only the current one.</summary>
    private void PruneOldIcons(Guid groupId, string keepPath)
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(_paths.IconsRoot, $"{groupId:N}*.ico"))
            {
                if (!string.Equals(stale, keepPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(stale);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover icon file is harmless.
        }
    }

    private static void WriteIfMissing(string iconPath, byte[] bytes)
    {
        try
        {
            if (File.Exists(iconPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(iconPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = iconPath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, iconPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write '{iconPath}': {ex.Message}");
        }
    }

    private byte[]? BuildIconBytes(Group group)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(group.IconPath) && File.Exists(group.IconPath))
            {
                // A user-supplied image still has to become a multi-size .ico, otherwise
                // the taskbar shows a blurry upscale of whatever size it happens to find.
                using var source = new System.Drawing.Bitmap(group.IconPath);
                return IcoWriter.Build(source);
            }

            using var glyph = GroupIconRenderer.RenderGroupGlyph(256, group.Name, group.Background, group.Emoji);
            return IcoWriter.Build(glyph);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException
                                      or System.Runtime.InteropServices.ExternalException)
        {
            Log.Warn($"Could not build the icon for '{group.Name}': {ex.Message}");
            return null;
        }
    }

    private static bool WriteIconIfChanged(string iconPath, byte[] bytes)
    {
        try
        {
            // Skip identical content: rewriting churns the shell icon cache for nothing.
            if (File.Exists(iconPath) && File.ReadAllBytes(iconPath).AsSpan().SequenceEqual(bytes))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(iconPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = iconPath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, iconPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write '{iconPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Every .lnk that stands for this group: the one this application generated, plus
    /// the copy Windows made when the user pinned it to the taskbar.
    /// </summary>
    private IEnumerable<string> FindLinksFor(Group group)
    {
        var expected = group.AppUserModelId;

        var folders = new[]
        {
            _paths.LinksRoot,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(folder, "*.lnk");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (string.Equals(
                        ShellLink.Instance.ReadAppUserModelId(candidate),
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Looks for an already-generated .lnk for this group by reading back the AUMID,
    /// which survives renaming the group.
    /// </summary>
    private string? FindExistingLink(Group group)
    {
        if (!Directory.Exists(_paths.LinksRoot))
        {
            return null;
        }

        var expected = group.AppUserModelId;

        foreach (var candidate in Directory.EnumerateFiles(_paths.LinksRoot, "*.lnk"))
        {
            if (string.Equals(ShellLink.Instance.ReadAppUserModelId(candidate), expected, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Removes the generated shortcut and icon of a deleted group.</summary>
    public void CleanUp(Group group)
    {
        try
        {
            var link = FindExistingLink(group);
            if (link is not null)
            {
                File.Delete(link);
            }

            // Every generated version, not just one name: the file carries a content hash.
            foreach (var icon in Directory.EnumerateFiles(_paths.IconsRoot, $"{group.Id:N}*.ico"))
            {
                File.Delete(icon);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not clean up files for '{group.Name}': {ex.Message}");
        }
    }

    /// <summary>Opens the Windows shortcut folder used for pinning, without selecting anything.</summary>
    public void OpenLinksFolder()
    {
        try
        {
            _paths.EnsureCreated();
            using var process = Process.Start(new ProcessStartInfo(_paths.LinksRoot) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Could not open the shortcuts folder", ex);
        }
    }
}
