using FluentAssertions;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class ShortcutResolverTests
{
    private const string ProgramFiles = @"C:\Program Files";

    private static ShortcutResolver ResolverFor(FakeFileSystem fs) =>
        new(fs, [ProgramFiles]);

    [Fact]
    public void Resolve_ReturnsOkWhenTheTargetIsWhereItShouldBe()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Thing\thing.exe");
        var shortcut = Shortcut.FromPath(@"C:\Program Files\Thing\thing.exe");

        var resolution = ResolverFor(fs).Resolve(shortcut);

        resolution.State.Should().Be(ShortcutState.Ok);
        resolution.CanLaunch.Should().BeTrue();
    }

    [Fact]
    public void Resolve_TreatsAPackagedAppAsLaunchableWithoutCheckingAnyPath()
    {
        var shortcut = new Shortcut { AppUserModelId = "Contoso_abc!App", DisplayName = "Contoso" };

        var resolution = ResolverFor(new FakeFileSystem()).Resolve(shortcut);

        resolution.State.Should().Be(ShortcutState.Packaged);
        resolution.CanLaunch.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ReportsInvalidWhenThereIsNeitherTargetNorAppId()
    {
        var resolution = ResolverFor(new FakeFileSystem()).Resolve(new Shortcut { DisplayName = "Broken" });

        resolution.State.Should().Be(ShortcutState.Invalid);
        resolution.CanLaunch.Should().BeFalse();
    }

    [Fact]
    public void Resolve_FindsAnApplicationThatMovedToANewVersionFolder()
    {
        // The classic case: an update replaces \app-1.2.0\ with \app-1.3.0\.
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Editor\app-1.3.0\editor.exe");

        var shortcut = new Shortcut
        {
            DisplayName = "Editor",
            Target = @"C:\Program Files\Editor\app-1.2.0\editor.exe",
            FallbackFileName = "editor.exe",
        };

        var resolution = ResolverFor(fs).Resolve(shortcut);

        resolution.State.Should().Be(ShortcutState.Relocated);
        resolution.ResolvedTarget.Should().Be(@"C:\Program Files\Editor\app-1.3.0\editor.exe");
        resolution.CanLaunch.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ReportsMissingWhenNothingMatchesAnywhere()
    {
        var fs = new FakeFileSystem().AddDirectory(ProgramFiles);

        var shortcut = new Shortcut
        {
            DisplayName = "Ghost",
            Target = @"C:\Program Files\Ghost\ghost.exe",
            FallbackFileName = "ghost.exe",
        };

        var resolution = ResolverFor(fs).Resolve(shortcut);

        resolution.State.Should().Be(ShortcutState.Missing);
        resolution.CanLaunch.Should().BeFalse();
        resolution.Message.Should().Contain("ghost.exe");
    }

    [Fact]
    public void TryRebind_WritesTheNewLocationBackIntoTheShortcut()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Editor\app-2.0\editor.exe");

        var shortcut = new Shortcut
        {
            DisplayName = "Editor",
            Target = @"C:\Program Files\Editor\app-1.0\editor.exe",
            WorkingDirectory = @"C:\Program Files\Editor\app-1.0",
            FallbackFileName = "editor.exe",
        };

        ResolverFor(fs).TryRebind(shortcut).Should().BeTrue();

        shortcut.Target.Should().Be(@"C:\Program Files\Editor\app-2.0\editor.exe");
        shortcut.WorkingDirectory.Should().Be(@"C:\Program Files\Editor\app-2.0");
    }

    [Fact]
    public void TryRebind_DoesNothingForAShortcutThatIsAlreadyFine()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Thing\thing.exe");
        var shortcut = Shortcut.FromPath(@"C:\Program Files\Thing\thing.exe");

        ResolverFor(fs).TryRebind(shortcut).Should().BeFalse();
        shortcut.Target.Should().Be(@"C:\Program Files\Thing\thing.exe");
    }

    [Fact]
    public void Resolve_StopsDescendingAtTheConfiguredDepth()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\a\b\c\d\e\f\deep.exe");

        var shortcut = new Shortcut
        {
            Target = @"C:\Elsewhere\deep.exe",
            FallbackFileName = "deep.exe",
        };

        var resolver = new ShortcutResolver(fs, [ProgramFiles]) { MaxSearchDepth = 2 };

        // A search that walked the whole drive would freeze the flyout; bounding the
        // depth means a very deeply nested match is reported as missing instead.
        resolver.Resolve(shortcut).State.Should().Be(ShortcutState.Missing);
    }
}
