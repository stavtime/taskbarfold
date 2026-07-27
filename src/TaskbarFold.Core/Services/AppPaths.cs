namespace TaskbarFold.Core.Services;

/// <summary>
/// Where TaskbarFold keeps its data. Everything lives under <c>%APPDATA%</c> so the
/// application never needs elevation to write its own configuration.
/// </summary>
public sealed class AppPaths
{
    public const string FolderName = "TaskbarFold";
    public const string ConfigFileName = "groups.json";

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName);
    }

    public static AppPaths Default { get; } = new();

    public string Root { get; }

    public string ConfigFile => Path.Combine(Root, ConfigFileName);

    /// <summary>Rendered PNGs, one folder per group.</summary>
    public string CacheRoot => Path.Combine(Root, "cache");

    /// <summary>Generated .ico files used by the pinned shortcuts.</summary>
    public string IconsRoot => Path.Combine(Root, "icons");

    /// <summary>Generated .lnk files the user pins to the taskbar.</summary>
    public string LinksRoot => Path.Combine(Root, "links");

    public string LogFile => Path.Combine(Root, "taskbarfold.log");

    public string CacheFolderFor(Guid groupId) => Path.Combine(CacheRoot, groupId.ToString("N"));

    public string IconFileFor(Guid groupId) => Path.Combine(IconsRoot, $"{groupId:N}.ico");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(IconsRoot);
        Directory.CreateDirectory(LinksRoot);
    }
}
