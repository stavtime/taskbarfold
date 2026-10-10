using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Services;

public enum ShortcutState
{
    /// <summary>Target exists at the recorded path.</summary>
    Ok,

    /// <summary>Packaged app; there is no path to verify, the shell resolves the AUMID.</summary>
    Packaged,

    /// <summary>Target moved but an identical file name was found elsewhere.</summary>
    Relocated,

    /// <summary>Target is gone and no replacement was found.</summary>
    Missing,

    /// <summary>Entry carries neither a target nor an AUMID.</summary>
    Invalid,
}

/// <param name="State">Outcome of the check.</param>
/// <param name="ResolvedTarget">Path to launch. For <see cref="ShortcutState.Relocated"/> this is the new path.</param>
/// <param name="Message">Human-readable explanation, suitable for showing in the UI.</param>
public sealed record ShortcutResolution(ShortcutState State, string? ResolvedTarget, string? Message = null)
{
    public bool CanLaunch => State is ShortcutState.Ok or ShortcutState.Packaged or ShortcutState.Relocated;
}

/// <summary>
/// Decides whether a shortcut can still be launched, and looks for a moved application
/// instead of leaving a permanently broken icon behind.
/// </summary>
public sealed class ShortcutResolver
{
    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<string> _searchRoots;

    public ShortcutResolver(IFileSystem? fileSystem = null, IReadOnlyList<string>? searchRoots = null)
    {
        _fs = fileSystem ?? PhysicalFileSystem.Instance;
        _searchRoots = searchRoots ?? DefaultSearchRoots();
    }

    /// <summary>How deep below a search root a relocated file is still looked for.</summary>
    public int MaxSearchDepth { get; init; } = 4;

    public ShortcutResolution Resolve(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        if (shortcut.Kind == ShortcutKind.Packaged)
        {
            return new ShortcutResolution(ShortcutState.Packaged, shortcut.AppUserModelId);
        }

        if (string.IsNullOrWhiteSpace(shortcut.Target))
        {
            return new ShortcutResolution(
                ShortcutState.Invalid, null, "The shortcut has no target and no application ID.");
        }

        if (_fs.FileExists(shortcut.Target) || _fs.DirectoryExists(shortcut.Target))
        {
            return new ShortcutResolution(ShortcutState.Ok, shortcut.Target);
        }

        var relocated = FindRelocated(shortcut);
        if (relocated is not null)
        {
            return new ShortcutResolution(
                ShortcutState.Relocated, relocated, $"Moved to {relocated}.");
        }

        return new ShortcutResolution(
            ShortcutState.Missing, null, $"{shortcut.Target} no longer exists.");
    }

    /// <summary>
    /// Applies a relocation to the shortcut so the fix survives the next save.
    /// Returns true when anything changed.
    /// </summary>
    public bool TryRebind(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        var resolution = Resolve(shortcut);
        if (resolution.State != ShortcutState.Relocated || resolution.ResolvedTarget is null)
        {
            return false;
        }

        shortcut.Target = resolution.ResolvedTarget;
        shortcut.WorkingDirectory = Path.GetDirectoryName(resolution.ResolvedTarget);
        return true;
    }

    private string? FindRelocated(Shortcut shortcut)
    {
        var fileName = shortcut.FallbackFileName;
        if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(shortcut.Target))
        {
            fileName = Path.GetFileName(shortcut.Target);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // A version bump usually moves the executable one level, so the original
        // directory's parent is by far the most likely place to look first.
        var originalDirectory = string.IsNullOrWhiteSpace(shortcut.Target)
            ? null
            : Path.GetDirectoryName(shortcut.Target);

        var roots = new List<string>();
        var grandparent = originalDirectory is null ? null : Path.GetDirectoryName(originalDirectory);
        if (grandparent is not null)
        {
            roots.Add(grandparent);
        }

        roots.AddRange(_searchRoots);

        foreach (var root in roots)
        {
            if (!_fs.DirectoryExists(root))
            {
                continue;
            }

            var hit = SearchDirectory(root, fileName, MaxSearchDepth);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    private string? SearchDirectory(string directory, string fileName, int depthRemaining)
    {
        foreach (var file in _fs.EnumerateFiles(directory, fileName))
        {
            return file;
        }

        if (depthRemaining <= 0)
        {
            return null;
        }

        foreach (var child in _fs.EnumerateDirectories(directory))
        {
            var hit = SearchDirectory(child, fileName, depthRemaining - 1);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    private static List<string> DefaultSearchRoots()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
