using System.Text.Json.Serialization;

namespace TaskbarFold.Core.Models;

/// <summary>
/// How a shortcut has to be launched. Packaged (Store/UWP) apps have no reachable
/// executable path — only an AppUserModelID resolvable through <c>shell:AppsFolder</c>.
/// </summary>
public enum ShortcutKind
{
    Desktop,
    Packaged,
}

/// <summary>One entry inside a group. Identity is the immutable <see cref="Id"/>.</summary>
public sealed class Shortcut
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Name shown under the icon. Free to edit, never used as identity.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Full path to the executable or document. Null for packaged apps.</summary>
    public string? Target { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    /// <summary>Set for Store/UWP apps; when present the shortcut is launched through the shell.</summary>
    public string? AppUserModelId { get; set; }

    /// <summary>
    /// File name of the original target, kept so a moved application can be found again
    /// instead of turning into a permanent broken icon.
    /// </summary>
    public string? FallbackFileName { get; set; }

    public bool RunAsAdmin { get; set; }

    /// <summary>Path of a user-supplied icon overriding the one extracted from the target.</summary>
    public string? IconPath { get; set; }

    [JsonIgnore]
    public ShortcutKind Kind =>
        string.IsNullOrWhiteSpace(AppUserModelId) ? ShortcutKind.Desktop : ShortcutKind.Packaged;

    public Shortcut Clone() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = DisplayName,
        Target = Target,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        AppUserModelId = AppUserModelId,
        FallbackFileName = FallbackFileName,
        RunAsAdmin = RunAsAdmin,
        IconPath = IconPath,
    };

    /// <summary>Builds a desktop shortcut from a file system path, filling the relocation hint.</summary>
    public static Shortcut FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        return new Shortcut
        {
            DisplayName = Path.GetFileNameWithoutExtension(full),
            Target = full,
            WorkingDirectory = Path.GetDirectoryName(full),
            FallbackFileName = Path.GetFileName(full),
        };
    }
}
