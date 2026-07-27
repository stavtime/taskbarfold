using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace TaskbarFold.Tests;

/// <summary>
/// The update feed is a contract with every installed copy of the application. These
/// tests pin the shape of that file and the version comparison, which is the one place a
/// mistake either nags everybody about a release they already have or hides a real one.
///
/// The parsing and comparison are mirrored here rather than referenced, because they live
/// in the WPF application project; the point is to lock the agreed behaviour of the feed.
/// </summary>
public sealed class UpdateManifestTests
{
    private sealed record Manifest(string? Version, string? Url, string? Notes);

    private static Manifest? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new Manifest(
            root.TryGetProperty("version", out var v) ? v.GetString() : null,
            root.TryGetProperty("url", out var u) ? u.GetString() : null,
            root.TryGetProperty("notes", out var n) ? n.GetString() : null);
    }

    private static Version Normalize(Version version) => new(
        version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static bool IsNewer(string published, Version installed) =>
        Version.TryParse(published.TrimStart('v', 'V'), out var parsed) &&
        Normalize(parsed) > Normalize(installed);

    [Fact]
    public void Feed_ParsesTheDocumentedShape()
    {
        var manifest = Parse("""
            {
              "version": "0.2.0",
              "url": "https://github.com/stavtime/taskbarfold/releases/tag/v0.2.0",
              "notes": "Emoji icons"
            }
            """);

        manifest!.Version.Should().Be("0.2.0");
        manifest.Url.Should().StartWith("https://");
        manifest.Notes.Should().Be("Emoji icons");
    }

    [Fact]
    public void Feed_ToleratesExtraFieldsAndAMissingNote()
    {
        // Adding fields later must not break copies already in the wild.
        var manifest = Parse("""
            { "version": "1.0.0", "url": "https://example.com/r", "channel": "stable", "minOs": "10.0.17763" }
            """);

        manifest!.Version.Should().Be("1.0.0");
        manifest.Notes.Should().BeNull();
    }

    [Theory]
    [InlineData("0.2.0", true)]
    [InlineData("v0.2.0", true)]
    [InlineData("0.1.1", true)]
    [InlineData("1.0.0", true)]
    [InlineData("0.1.0", false)]
    [InlineData("0.0.9", false)]
    public void Comparison_DetectsOnlyGenuinelyNewerReleases(string published, bool expected)
    {
        IsNewer(published, new Version(0, 1, 0, 0)).Should().Be(expected);
    }

    [Fact]
    public void Comparison_TreatsThreePartAndFourPartVersionsAsEqual()
    {
        // Version.Parse leaves unspecified fields as -1, so "0.1.0" would otherwise sort
        // below the assembly's "0.1.0.0" and a matching release would look older.
        IsNewer("0.1.0", new Version(0, 1, 0, 0)).Should().BeFalse();
        IsNewer("0.1.0.0", new Version(0, 1, 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("2026-07-27")]
    [InlineData("0.2.0-beta")]
    public void Comparison_RejectsVersionsItCannotUnderstand(string published)
    {
        // A malformed feed must never be read as "an update is available".
        IsNewer(published, new Version(0, 1, 0, 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://taskbarfold.duckapps.ru/download", true)]
    [InlineData("http://taskbarfold.duckapps.ru/download", false)]
    [InlineData("file:///C:/evil.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    public void ReleaseUrl_IsAcceptedOnlyWhenAbsoluteHttps(string url, bool expected)
    {
        // The feed is remote input: whatever it says, only an absolute HTTPS link is
        // ever handed to the shell.
        var ok = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        ok.Should().Be(expected);
    }
}
