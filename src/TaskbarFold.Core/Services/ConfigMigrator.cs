using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Services;

public enum MigrationOutcome
{
    /// <summary>Document already matched the current schema.</summary>
    UpToDate,

    /// <summary>Document was written by an older build and was upgraded in memory.</summary>
    Upgraded,

    /// <summary>Document was written by a newer build; loading it would lose data.</summary>
    TooNew,
}

/// <summary>
/// Brings a loaded document up to <see cref="GroupsDocument.CurrentVersion"/>.
/// Every future schema change adds one step here rather than mutating the models.
/// </summary>
public static class ConfigMigrator
{
    public static MigrationOutcome Migrate(GroupsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Version > GroupsDocument.CurrentVersion)
        {
            return MigrationOutcome.TooNew;
        }

        var upgraded = false;

        // Version 0 is anything written before the schema carried a version field.
        if (document.Version < 1)
        {
            document.Version = 1;
            upgraded = true;
        }

        upgraded |= Normalize(document);

        return upgraded ? MigrationOutcome.Upgraded : MigrationOutcome.UpToDate;
    }

    /// <summary>
    /// Repairs values a hand-edited or partially written file may be missing, so the
    /// rest of the application can treat the document as trustworthy.
    /// </summary>
    private static bool Normalize(GroupsDocument document)
    {
        var changed = false;

        // Deserialization can leave these null when the JSON omits or nulls the member,
        // even though the property type says otherwise.
        if (document.Settings is null)
        {
            document.Settings = new AppSettings();
            changed = true;
        }

        if (document.Groups is null)
        {
            document.Groups = [];
            changed = true;
        }

        var seenGroupIds = new HashSet<Guid>();

        // JSON arrays can contain null elements even when the C# element type is non-nullable.
        changed |= document.Groups.RemoveAll(group => group is null) > 0;

        foreach (var group in document.Groups)
        {
            if (group.Id == Guid.Empty || !seenGroupIds.Add(group.Id))
            {
                group.Id = Guid.NewGuid();
                seenGroupIds.Add(group.Id);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                group.Name = "Group";
                changed = true;
            }

            if (group.Shortcuts is null)
            {
                group.Shortcuts = [];
                changed = true;
            }

            var seenShortcutIds = new HashSet<Guid>();

            changed |= group.Shortcuts.RemoveAll(shortcut => shortcut is null) > 0;

            foreach (var shortcut in group.Shortcuts)
            {
                if (shortcut.Id == Guid.Empty || !seenShortcutIds.Add(shortcut.Id))
                {
                    shortcut.Id = Guid.NewGuid();
                    seenShortcutIds.Add(shortcut.Id);
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(shortcut.DisplayName))
                {
                    shortcut.DisplayName = DeriveName(shortcut);
                    changed = true;
                }

                // Older entries predate the relocation hint; fill it from the target.
                if (string.IsNullOrWhiteSpace(shortcut.FallbackFileName) &&
                    !string.IsNullOrWhiteSpace(shortcut.Target))
                {
                    shortcut.FallbackFileName = Path.GetFileName(shortcut.Target);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static string DeriveName(Shortcut shortcut)
    {
        if (!string.IsNullOrWhiteSpace(shortcut.Target))
        {
            return Path.GetFileNameWithoutExtension(shortcut.Target);
        }

        return string.IsNullOrWhiteSpace(shortcut.AppUserModelId) ? "Shortcut" : shortcut.AppUserModelId;
    }
}
