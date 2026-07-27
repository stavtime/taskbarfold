using System.Text.Json.Serialization;

namespace TaskbarFold.Core.Models;

/// <summary>
/// A taskbar mini-folder. The <see cref="Id"/> is the identity used for the
/// AppUserModelID, the .lnk file name and the icon cache folder, so renaming a
/// group never breaks its pin on the taskbar.
/// </summary>
public sealed class Group
{
    public const int MinColumns = 1;
    public const int MaxColumns = 12;

    private int _columns = 5;
    private double _opacity = 0.92;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New group";

    /// <summary>Path of the .ico used for the taskbar pin and the tile in settings.</summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Emoji drawn on the group tile, e.g. "🎮". Takes effect when no <see cref="IconPath"/>
    /// is set; without either, the tile falls back to the group's initial.
    /// </summary>
    public string? Emoji { get; set; }

    public int Columns
    {
        get => _columns;
        set => _columns = Math.Clamp(value, MinColumns, MaxColumns);
    }

    /// <summary>Flyout background, <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public string Background { get; set; } = "#1F1F1F";

    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.2, 1.0);
    }

    /// <summary>Optional global hotkey, e.g. <c>Ctrl+Alt+D1</c>. Null when unbound.</summary>
    public string? Hotkey { get; set; }

    public List<Shortcut> Shortcuts { get; set; } = [];

    /// <summary>
    /// Unique per group. Without it Windows collapses every pinned group into a
    /// single taskbar button, because they all point at the same executable.
    /// </summary>
    [JsonIgnore]
    public string AppUserModelId => GroupIdentity.AppUserModelIdFor(Id);

    public Group Clone(string? newName = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = newName ?? $"{Name} (copy)",
        IconPath = IconPath,
        Emoji = Emoji,
        Columns = Columns,
        Background = Background,
        Opacity = Opacity,
        Hotkey = null,
        Shortcuts = Shortcuts.Select(s => s.Clone()).ToList(),
    };
}

/// <summary>Derives the stable Windows-facing identifiers of a group from its GUID.</summary>
public static class GroupIdentity
{
    public const string AppUserModelIdPrefix = "TaskbarFold.Group.";

    public static string AppUserModelIdFor(Guid groupId) =>
        AppUserModelIdPrefix + groupId.ToString("N");

    /// <summary>File name (without directory) of the .lnk that gets pinned to the taskbar.</summary>
    public static string LinkFileNameFor(Group group)
    {
        var safe = Sanitize(group.Name);
        return safe.Length == 0 ? $"{group.Id:N}.lnk" : $"{safe}.lnk";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim()
            .Where(c => !invalid.Contains(c) && c is not '.')
            .ToArray();
        return new string(chars).Trim();
    }
}
