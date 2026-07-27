using FluentAssertions;
using TaskbarFold.Core.Models;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class ModelTests
{
    [Fact]
    public void AppUserModelId_IsDerivedFromTheGroupId_AndSurvivesRenaming()
    {
        var group = new Group { Name = "Work" };
        var before = group.AppUserModelId;

        group.Name = "Something else entirely";

        group.AppUserModelId.Should().Be(before);
        group.AppUserModelId.Should().Be(GroupIdentity.AppUserModelIdPrefix + group.Id.ToString("N"));
    }

    [Fact]
    public void AppUserModelId_IsDistinctPerGroup()
    {
        // Two groups sharing an AUMID collapse into one taskbar button.
        new Group().AppUserModelId.Should().NotBe(new Group().AppUserModelId);
    }

    [Theory]
    [InlineData(0, Group.MinColumns)]
    [InlineData(-4, Group.MinColumns)]
    [InlineData(5, 5)]
    [InlineData(99, Group.MaxColumns)]
    public void Columns_IsClampedToASaneRange(int assigned, int expected)
    {
        new Group { Columns = assigned }.Columns.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(0.5, 0.5)]
    [InlineData(3.0, 1.0)]
    public void Opacity_IsClamped(double assigned, double expected)
    {
        new Group { Opacity = assigned }.Opacity.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void LinkFileName_StripsCharactersWindowsWouldReject()
    {
        var group = new Group { Name = @"Work: <2026>/Q1" };

        var fileName = GroupIdentity.LinkFileNameFor(group);

        fileName.Should().NotContainAny(":", "<", ">", "/", "\\", "*", "?", "\"", "|");
        fileName.Should().EndWith(".lnk");
    }

    [Fact]
    public void LinkFileName_FallsBackToTheIdWhenTheNameIsAllPunctuation()
    {
        var group = new Group { Name = "///" };

        GroupIdentity.LinkFileNameFor(group).Should().Be($"{group.Id:N}.lnk");
    }

    [Fact]
    public void Clone_ProducesANewIdentityForTheGroupAndEveryShortcut()
    {
        var original = new Group
        {
            Name = "Utilities",
            Hotkey = "Ctrl+Alt+U",
            Shortcuts = { new Shortcut { DisplayName = "Notepad", Target = @"C:\Windows\notepad.exe" } },
        };

        var copy = original.Clone();

        copy.Id.Should().NotBe(original.Id);
        copy.Shortcuts[0].Id.Should().NotBe(original.Shortcuts[0].Id);
        copy.Shortcuts[0].Target.Should().Be(original.Shortcuts[0].Target);

        // A duplicated group must not steal the original's hotkey registration.
        copy.Hotkey.Should().BeNull();
    }

    [Fact]
    public void FromPath_FillsTheNameWorkingFolderAndRelocationHint()
    {
        var shortcut = Shortcut.FromPath(@"C:\Program Files\Thing\thing.exe");

        shortcut.DisplayName.Should().Be("thing");
        shortcut.Target.Should().Be(@"C:\Program Files\Thing\thing.exe");
        shortcut.WorkingDirectory.Should().Be(@"C:\Program Files\Thing");
        shortcut.FallbackFileName.Should().Be("thing.exe");
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom, true)]
    [InlineData(TaskbarEdge.Top, true)]
    [InlineData(TaskbarEdge.Left, false)]
    [InlineData(TaskbarEdge.Right, false)]
    public void TaskbarLayout_KnowsItsOrientation(TaskbarEdge edge, bool horizontal)
    {
        var layout = new TaskbarLayout(
            edge,
            new PixelRect(0, 1032, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1032),
            AutoHide: false,
            Dpi: 96);

        layout.IsHorizontal.Should().Be(horizontal);
    }

    [Fact]
    public void TaskbarLayout_ReportsThicknessAlongTheDockedAxis()
    {
        var layout = new TaskbarLayout(
            TaskbarEdge.Left,
            new PixelRect(0, 0, 62, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(62, 0, 1920, 1080),
            AutoHide: false,
            Dpi: 144);

        layout.Thickness.Should().Be(62);
        layout.Scale.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void TaskbarLayout_GivesAnAutoHiddenBarANonZeroThickness()
    {
        // An auto-hidden taskbar reports a sliver a couple of pixels tall. Using that
        // directly would put the flyout flush against the screen edge.
        var layout = new TaskbarLayout(
            TaskbarEdge.Bottom,
            new PixelRect(0, 1080, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            AutoHide: true,
            Dpi: 192);

        layout.Thickness.Should().BeGreaterThan(0);
    }
}
