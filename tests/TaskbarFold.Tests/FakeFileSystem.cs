using TaskbarFold.Core.Services;

namespace TaskbarFold.Tests;

/// <summary>In-memory <see cref="IFileSystem"/> so path logic can be tested without disk.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, (DateTime Written, long Length)> _files =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem AddFile(string path, DateTime? written = null, long length = 1024)
    {
        _files[path] = (written ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), length);

        // Register every ancestor so enumeration behaves like a real tree.
        var directory = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
            directory = Path.GetDirectoryName(directory);
        }

        return this;
    }

    public FakeFileSystem AddDirectory(string path)
    {
        _directories.Add(path);
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        _directories.Where(d => IsDirectChild(path, d));

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern) =>
        _files.Keys.Where(f =>
            string.Equals(Path.GetDirectoryName(f), path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(f), searchPattern, StringComparison.OrdinalIgnoreCase));

    public DateTime GetLastWriteTimeUtc(string path) =>
        _files.TryGetValue(path, out var entry) ? entry.Written : DateTime.MinValue;

    public long GetFileLength(string path) =>
        _files.TryGetValue(path, out var entry) ? entry.Length : -1;

    private static bool IsDirectChild(string parent, string candidate) =>
        string.Equals(Path.GetDirectoryName(candidate), parent, StringComparison.OrdinalIgnoreCase);
}
