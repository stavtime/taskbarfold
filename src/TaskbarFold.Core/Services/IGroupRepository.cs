using TaskbarFold.Core.Models;

namespace TaskbarFold.Core.Services;

public enum ConfigLoadStatus
{
    /// <summary>File read and parsed as-is.</summary>
    Loaded,

    /// <summary>No config file yet; a default document was returned.</summary>
    Created,

    /// <summary>File was parsed and upgraded from an older schema version.</summary>
    Migrated,

    /// <summary>File was unreadable or malformed; it was set aside and a default returned.</summary>
    Recovered,

    /// <summary>File was written by a newer build; it was left untouched and a default returned.</summary>
    TooNew,
}

/// <param name="Document">Always usable, even when loading failed.</param>
/// <param name="Status">What actually happened, so the UI can warn instead of silently losing groups.</param>
/// <param name="BackupPath">Where a damaged file was moved, when applicable.</param>
/// <param name="Error">Parser or IO message behind a <see cref="ConfigLoadStatus.Recovered"/> result.</param>
public sealed record ConfigLoadResult(
    GroupsDocument Document,
    ConfigLoadStatus Status,
    string? BackupPath = null,
    string? Error = null);

public interface IGroupRepository
{
    string ConfigFilePath { get; }

    ConfigLoadResult Load();

    /// <summary>Writes the document atomically. A crash mid-write never leaves a truncated config.</summary>
    void Save(GroupsDocument document);
}
