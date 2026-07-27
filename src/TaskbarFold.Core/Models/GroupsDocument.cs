namespace TaskbarFold.Core.Models;

/// <summary>Root of <c>groups.json</c>.</summary>
public sealed class GroupsDocument
{
    /// <summary>Schema version written by this build. Bump together with a migration step.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public AppSettings Settings { get; set; } = new();

    public List<Group> Groups { get; set; } = [];

    public Group? Find(Guid id) => Groups.FirstOrDefault(g => g.Id == id);
}
