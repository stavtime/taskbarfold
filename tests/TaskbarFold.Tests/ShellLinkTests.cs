using FluentAssertions;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using Xunit;

namespace TaskbarFold.Tests;

/// <summary>
/// Exercises the real shell COM objects. These are the pieces that decide whether a
/// pinned group works at all, and they cannot be meaningfully faked.
/// </summary>
public sealed class ShellLinkTests
{
    private static string Notepad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

    [Fact]
    public void CreateOrUpdate_WritesALinkCarryingTheAppUserModelId()
    {
        using var temp = new TestDirectory();
        var linkPath = temp.File("group.lnk");
        var group = new Group { Name = "Work" };

        ShellLink.Instance.CreateOrUpdate(linkPath, new ShellLinkData(
            TargetPath: Notepad,
            Arguments: $"--group {group.Id}",
            Description: "TaskbarFold group: Work",
            AppUserModelId: group.AppUserModelId));

        File.Exists(linkPath).Should().BeTrue();

        // Without a distinct AUMID Windows merges every pinned group into one button,
        // so this property is the whole reason the .lnk is generated rather than copied.
        ShellLink.Instance.ReadAppUserModelId(linkPath).Should().Be(group.AppUserModelId);
    }

    [Fact]
    public void CreateOrUpdate_EditsAnExistingLinkInPlace()
    {
        using var temp = new TestDirectory();
        var linkPath = temp.File("group.lnk");
        var group = new Group { Name = "Work" };

        ShellLink.Instance.CreateOrUpdate(linkPath, new ShellLinkData(
            TargetPath: Notepad,
            Arguments: $"--group {group.Id}",
            AppUserModelId: group.AppUserModelId));

        var createdAt = File.GetCreationTimeUtc(linkPath);

        // Renaming a group must not recreate the file: Windows tracks the taskbar pin by
        // the shortcut's identity, so a delete-and-recreate silently unpins the group.
        group.Name = "Work (renamed)";
        ShellLink.Instance.CreateOrUpdate(linkPath, new ShellLinkData(
            TargetPath: Notepad,
            Arguments: $"--group {group.Id}",
            Description: "TaskbarFold group: Work (renamed)",
            AppUserModelId: group.AppUserModelId));

        File.GetCreationTimeUtc(linkPath).Should().Be(createdAt);
        ShellLink.Instance.ReadAppUserModelId(linkPath).Should().Be(group.AppUserModelId);
    }

    [Fact]
    public void ReadAppUserModelId_ReturnsNullForAMissingFile()
    {
        using var temp = new TestDirectory();

        ShellLink.Instance.ReadAppUserModelId(temp.File("nope.lnk")).Should().BeNull();
    }

    [Fact]
    public void IconExtractor_ProducesAnIconForARealExecutable()
    {
        var image = new IconExtractor().ExtractFromPath(Notepad, 64);

        image.Should().NotBeNull();
        image!.Width.Should().Be(64);
        image.PngBytes.Should().NotBeEmpty();

        // PNG signature, so the bytes really are a decodable image.
        image.PngBytes.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
    }

    [Fact]
    public void IconExtractor_ReturnsNullForANonexistentPath()
    {
        new IconExtractor()
            .ExtractFromPath(@"C:\definitely\not\here\nothing.exe", 32)
            .Should().BeNull();
    }

    [Fact]
    public void TaskbarInfo_DescribesTheTaskbarOfTheMonitorUnderAPoint()
    {
        var layout = TaskbarInfo.Instance.GetLayoutAt(10, 10);

        layout.Should().NotBeNull();
        layout.Dpi.Should().BeGreaterThan(0);
        layout.MonitorBounds.Width.Should().BeGreaterThan(0);
        layout.MonitorBounds.Height.Should().BeGreaterThan(0);
        layout.Thickness.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AppxResolver_EnumeratesInstalledApplications()
    {
        var apps = new AppxResolver().GetInstalledApps();

        // Every Windows install has entries in the Apps folder.
        apps.Should().NotBeEmpty();
        apps.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.AppUserModelId));
        apps.Should().Contain(a => a.IsPackaged);
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData(@"C:\Windows\notepad.exe", false)]
    [InlineData("", false)]
    public void IsPackagedAppUserModelId_RecognisesThePackageForm(string aumid, bool expected)
    {
        AppxResolver.IsPackagedAppUserModelId(aumid).Should().Be(expected);
    }
}
