using System.Collections.Concurrent;
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
///
/// Looking for a moved application walks a good part of the disk, a few hundred
/// milliseconds at best, so <see cref="Check"/> is the only member safe on a path the user
/// is waiting for. <see cref="Resolve"/> may be called from any thread.
/// </summary>
public sealed class ShortcutResolver
{
    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<string> _searchRoots;

    /// <summary>When each fruitless search ended, in <see cref="Environment.TickCount64"/> terms.</summary>
    private readonly ConcurrentDictionary<string, long> _recentMisses = new(StringComparer.OrdinalIgnoreCase);

    public ShortcutResolver(IFileSystem? fileSystem = null, IReadOnlyList<string>? searchRoots = null)
    {
        _fs = fileSystem ?? PhysicalFileSystem.Instance;
        _searchRoots = searchRoots ?? DefaultSearchRoots();
    }

    /// <summary>How deep below a search root a relocated file is still looked for.</summary>
    public int MaxSearchDepth { get; init; } = 4;

    /// <summary>
    /// How long a search that found nothing is not repeated. An uninstalled application
    /// would otherwise cost a full walk of every search root each time it is looked at.
    /// </summary>
    public TimeSpan MissMemory { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Reports the shortcut's state from the recorded path alone, without searching. A
    /// target that moved is reported as <see cref="ShortcutState.Missing"/>.
    /// </summary>
    public ShortcutResolution Check(Shortcut shortcut)
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

        return new ShortcutResolution(
            ShortcutState.Missing, null, $"{shortcut.Target} no longer exists.");
    }

    /// <summary>
    /// Like <see cref="Check"/>, but goes looking for a target that is gone.
    /// </summary>
    /// <param name="shortcut">The shortcut to examine. It is not modified.</param>
    /// <param name="ignoreRecentMiss">
    /// Search even if the last search came up empty, for when the user explicitly asks.
    /// </param>
    public ShortcutResolution Resolve(Shortcut shortcut, bool ignoreRecentMiss = false)
    {
        var resolution = Check(shortcut);
        if (resolution.State != ShortcutState.Missing)
        {
            return resolution;
        }

        if (!ignoreRecentMiss && RecentlyMissed(shortcut))
        {
            return resolution;
        }

        var relocated = FindRelocated(shortcut);
        if (relocated is null)
        {
            _recentMisses[MissKey(shortcut)] = Environment.TickCount64;
            return resolution;
        }

        _recentMisses.TryRemove(MissKey(shortcut), out _);
        return new ShortcutResolution(
            ShortcutState.Relocated, relocated, $"Moved to {relocated}.");
    }

    /// <summary>True while a recent fruitless search makes another one pointless.</summary>
    public bool RecentlyMissed(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        return _recentMisses.TryGetValue(MissKey(shortcut), out var missedAt) &&
               Environment.TickCount64 - missedAt < MissMemory.TotalMilliseconds;
    }

    private static string MissKey(Shortcut shortcut) => $"{shortcut.Target}|{shortcut.FallbackFileName}";

    /// <summary>
    /// Applies a relocation found by <see cref="Resolve"/> to the shortcut, so the fix
    /// survives the next save and the search never has to run for it again.
    /// Returns true when anything changed.
    /// </summary>
    public static bool TryRebind(Shortcut shortcut, ShortcutResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentNullException.ThrowIfNull(resolution);

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
