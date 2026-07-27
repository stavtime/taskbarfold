using System.Text.Json.Serialization;

namespace TaskbarFold.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Application-wide preferences. Stored alongside the groups in the same document.</summary>
public sealed class AppSettings
{
    private int _iconSize = 48;

    public bool RunAtStartup { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Icon edge length in device-independent pixels.</summary>
    public int IconSize
    {
        get => _iconSize;
        set => _iconSize = Math.Clamp(value, 24, 96);
    }

    public bool ShowLabels { get; set; } = true;

    public bool CloseOnLaunch { get; set; } = true;

    public bool TypeToSearch { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Timestamp of the last successful update check; the check is throttled to once a day.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}
