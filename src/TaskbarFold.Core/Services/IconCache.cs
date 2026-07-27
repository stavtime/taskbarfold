using System.Globalization;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Services;

/// <summary>
/// Keeps rendered PNGs on disk at <c>cache/&lt;group&gt;/&lt;shortcut&gt;.png</c>.
///
/// Extracting an icon costs a shell round-trip, and the flyout is expected to appear
/// in well under the time a click takes. Entries are invalidated by the source file's
/// last-write time and length, so an application update refreshes its icon without the
/// user having to clear anything.
/// </summary>
public sealed class IconCache
{
    private readonly AppPaths _paths;
    private readonly IIconExtractor _extractor;
    private readonly IFileSystem _fs;
    private readonly object _gate = new();

    public IconCache(
        AppPaths? paths = null,
        IIconExtractor? extractor = null,
        IFileSystem? fileSystem = null)
    {
        _paths = paths ?? AppPaths.Default;
        _extractor = extractor ?? new IconExtractor();
        _fs = fileSystem ?? PhysicalFileSystem.Instance;
    }

    /// <summary>Edge length in pixels of the cached image. Large enough for 200% scaling.</summary>
    public int RenderSize { get; init; } = 128;

    /// <summary>
    /// Returns the path of a cached PNG for the shortcut, rendering it if needed.
    /// Returns null when no icon could be produced at all.
    /// </summary>
    public string? GetOrCreate(Guid groupId, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        var folder = _paths.CacheFolderFor(groupId);
        var target = Path.Combine(folder, $"{shortcut.Id:N}.png");
        var stampFile = target + ".stamp";
        var stamp = ComputeStamp(shortcut);

        lock (_gate)
        {
            if (_fs.FileExists(target) && StampMatches(stampFile, stamp))
            {
                return target;
            }

            var image = Render(shortcut);
            if (image is null)
            {
                return _fs.FileExists(target) ? target : null;
            }

            try
            {
                Directory.CreateDirectory(folder);

                var temp = target + ".tmp";
                File.WriteAllBytes(temp, image.PngBytes);
                File.Move(temp, target, overwrite: true);
                File.WriteAllText(stampFile, stamp);
                return target;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Renders every icon of every group so the first flyout has nothing to wait for.</summary>
    public void Warm(IEnumerable<Group> groups, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);

        foreach (var group in groups)
        {
            foreach (var shortcut in group.Shortcuts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GetOrCreate(group.Id, shortcut);
            }
        }
    }

    /// <summary>Drops the cache folder of a deleted group.</summary>
    public void Evict(Guid groupId)
    {
        try
        {
            var folder = _paths.CacheFolderFor(groupId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale cache folder is harmless; failing to remove it must not break deletion.
        }
    }

    public void Evict(Guid groupId, Guid shortcutId)
    {
        try
        {
            var target = Path.Combine(_paths.CacheFolderFor(groupId), $"{shortcutId:N}.png");
            File.Delete(target);
            File.Delete(target + ".stamp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private IconImage? Render(Shortcut shortcut)
    {
        if (!string.IsNullOrWhiteSpace(shortcut.IconPath) && _fs.FileExists(shortcut.IconPath))
        {
            var custom = _extractor.ExtractFromPath(shortcut.IconPath, RenderSize);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (shortcut.Kind == ShortcutKind.Packaged && !string.IsNullOrWhiteSpace(shortcut.AppUserModelId))
        {
            return _extractor.ExtractFromAppUserModelId(shortcut.AppUserModelId, RenderSize);
        }

        if (!string.IsNullOrWhiteSpace(shortcut.Target) && _fs.FileExists(shortcut.Target))
        {
            return _extractor.ExtractFromPath(shortcut.Target, RenderSize);
        }

        return null;
    }

    /// <summary>
    /// Identity of the rendered source. Any change to the source path, its timestamp,
    /// its size or the render size produces a different stamp and forces a re-render.
    /// </summary>
    private string ComputeStamp(Shortcut shortcut)
    {
        var source = !string.IsNullOrWhiteSpace(shortcut.IconPath) && _fs.FileExists(shortcut.IconPath)
            ? shortcut.IconPath
            : shortcut.Target;

        if (string.IsNullOrWhiteSpace(source) || !_fs.FileExists(source))
        {
            // Packaged apps have no file to stamp; key on the AUMID instead.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"aumid|{shortcut.AppUserModelId}|{RenderSize}");
        }

        var ticks = _fs.GetLastWriteTimeUtc(source).Ticks;
        var length = _fs.GetFileLength(source);
        return string.Create(CultureInfo.InvariantCulture, $"{source}|{ticks}|{length}|{RenderSize}");
    }

    private static bool StampMatches(string stampFile, string expected)
    {
        try
        {
            return File.Exists(stampFile) &&
                   string.Equals(File.ReadAllText(stampFile), expected, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
