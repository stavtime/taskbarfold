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
    public void Resolve_AllowsAnExistingFolderAddedFromItsPath()
    {
        using var temp = new TestDirectory();
        var folder = Path.Combine(temp.Path, "My documents");
        Directory.CreateDirectory(folder);
        var shortcut = Shortcut.FromPath(folder);
        var resolver = new ShortcutResolver(searchRoots: []);

        var resolution = resolver.Resolve(shortcut);

        resolution.State.Should().Be(ShortcutState.Ok);
        resolution.ResolvedTarget.Should().Be(folder);
        resolution.CanLaunch.Should().BeTrue();
        ShortcutResolver.TryRebind(shortcut, resolution).Should().BeFalse();
        shortcut.Target.Should().Be(folder);
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

        var resolution = ResolverFor(fs).Resolve(shortcut);

        ShortcutResolver.TryRebind(shortcut, resolution).Should().BeTrue();

        shortcut.Target.Should().Be(@"C:\Program Files\Editor\app-2.0\editor.exe");
        shortcut.WorkingDirectory.Should().Be(@"C:\Program Files\Editor\app-2.0");
    }

    [Fact]
    public void TryRebind_DoesNothingForAShortcutThatIsAlreadyFine()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Thing\thing.exe");
        var shortcut = Shortcut.FromPath(@"C:\Program Files\Thing\thing.exe");

        var resolution = ResolverFor(fs).Resolve(shortcut);

        ShortcutResolver.TryRebind(shortcut, resolution).Should().BeFalse();
        shortcut.Target.Should().Be(@"C:\Program Files\Thing\thing.exe");
    }

    [Fact]
    public void Check_ReportsAMovedApplicationAsMissingWithoutSearching()
    {
        var fs = new CountingFileSystem(new FakeFileSystem().AddFile(@"C:\Program Files\Editor\app-2.0\editor.exe"));

        var resolution = new ShortcutResolver(fs, [ProgramFiles]).Check(MovedEditor());

        resolution.State.Should().Be(ShortcutState.Missing);
        resolution.CanLaunch.Should().BeFalse();
        fs.Enumerations.Should().Be(0);
    }

    [Fact]
    public void Resolve_DoesNotRepeatASearchThatJustFoundNothing()
    {
        var inner = new FakeFileSystem().AddDirectory(ProgramFiles);
        var fs = new CountingFileSystem(inner);
        var resolver = new ShortcutResolver(fs, [ProgramFiles]);

        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Missing);
        var afterFirstSearch = fs.Enumerations;
        resolver.RecentlyMissed(MovedEditor()).Should().BeTrue();

        // Even an application that has since appeared waits out the memory...
        inner.AddFile(@"C:\Program Files\Editor\app-2.0\editor.exe");
        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Missing);

        afterFirstSearch.Should().BeGreaterThan(0);
        fs.Enumerations.Should().Be(afterFirstSearch);

        // ...unless the user explicitly asks to look again.
        var resolution = resolver.Resolve(MovedEditor(), ignoreRecentMiss: true);
        resolution.State.Should().Be(ShortcutState.Relocated);

        // A success clears the memory, so the next ordinary call searches too.
        resolver.RecentlyMissed(MovedEditor()).Should().BeFalse();
        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Relocated);
    }

    [Fact]
    public void Resolve_SearchesAgainOnceTheMemoryOfAMissHasExpired()
    {
        var fs = new FakeFileSystem().AddDirectory(ProgramFiles);
        var resolver = new ShortcutResolver(fs, [ProgramFiles]) { MissMemory = TimeSpan.Zero };

        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Missing);

        fs.AddFile(@"C:\Program Files\Editor\app-2.0\editor.exe");
        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Relocated);
    }

    [Fact]
    public void Resolve_RemembersAMissPerShortcutRatherThanForAllOfThem()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Other\other.exe");
        var resolver = new ShortcutResolver(fs, [ProgramFiles]);

        resolver.Resolve(MovedEditor()).State.Should().Be(ShortcutState.Missing);

        var other = new Shortcut
        {
            Target = @"C:\Program Files\Other\old\other.exe",
            FallbackFileName = "other.exe",
        };

        resolver.Resolve(other).State.Should().Be(ShortcutState.Relocated);
    }

    [Fact]
    public void TryRebind_MakesLaterChecksReportTheShortcutAsFine()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\Program Files\Editor\app-2.0\editor.exe");
        var resolver = ResolverFor(fs);
        var shortcut = MovedEditor();

        ShortcutResolver.TryRebind(shortcut, resolver.Resolve(shortcut)).Should().BeTrue();

        resolver.Check(shortcut).State.Should().Be(ShortcutState.Ok);
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

    private static Shortcut MovedEditor() => new()
    {
        DisplayName = "Editor",
        Target = @"C:\Program Files\Editor\app-1.0\editor.exe",
        FallbackFileName = "editor.exe",
    };

    /// <summary>Counts the directory listings a search performs.</summary>
    private sealed class CountingFileSystem(FakeFileSystem inner) : IFileSystem
    {
        public int Enumerations { get; private set; }

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            Enumerations++;
            return inner.EnumerateDirectories(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            Enumerations++;
            return inner.EnumerateFiles(path, searchPattern);
        }

        public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

        public long GetFileLength(string path) => inner.GetFileLength(path);
    }
}
