using FluentAssertions;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class IconCacheTests
{
    /// <summary>Counts extractions so cache hits and misses can be told apart.</summary>
    private sealed class CountingExtractor : IIconExtractor
    {
        public int PathCalls { get; private set; }

        public int AumidCalls { get; private set; }

        public bool ReturnNothing { get; set; }

        public IconImage? ExtractFromPath(string path, int size)
        {
            PathCalls++;
            return ReturnNothing ? null : new IconImage(size, size, [1, 2, 3, 4]);
        }

        public IconImage? ExtractFromAppUserModelId(string appUserModelId, int size)
        {
            AumidCalls++;
            return ReturnNothing ? null : new IconImage(size, size, [5, 6, 7, 8]);
        }
    }

    [Fact]
    public void GetOrCreate_RendersOnceAndServesTheCachedFileAfterwards()
    {
        using var temp = new TestDirectory();
        var source = temp.File("app.exe");
        File.WriteAllText(source, "binary");

        var extractor = new CountingExtractor();
        var cache = new IconCache(new AppPaths(temp.Path), extractor);
        var groupId = Guid.NewGuid();
        var shortcut = Shortcut.FromPath(source);

        var first = cache.GetOrCreate(groupId, shortcut);
        var second = cache.GetOrCreate(groupId, shortcut);

        first.Should().NotBeNull();
        second.Should().Be(first);
        File.Exists(first!).Should().BeTrue();
        extractor.PathCalls.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_RerendersAfterTheSourceFileChanges()
    {
        using var temp = new TestDirectory();
        var source = temp.File("app.exe");
        File.WriteAllText(source, "v1");

        var extractor = new CountingExtractor();
        var cache = new IconCache(new AppPaths(temp.Path), extractor);
        var groupId = Guid.NewGuid();
        var shortcut = Shortcut.FromPath(source);

        cache.GetOrCreate(groupId, shortcut);

        // An application update rewrites the executable; the icon has to follow.
        File.WriteAllText(source, "v2 is a different length");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(5));

        cache.GetOrCreate(groupId, shortcut);

        extractor.PathCalls.Should().Be(2);
    }

    [Fact]
    public void GetOrCreate_UsesTheAppIdPathForPackagedApps()
    {
        using var temp = new TestDirectory();
        var extractor = new CountingExtractor();
        var cache = new IconCache(new AppPaths(temp.Path), extractor);

        var shortcut = new Shortcut { DisplayName = "Calc", AppUserModelId = "Contoso_abc!App" };

        cache.GetOrCreate(Guid.NewGuid(), shortcut).Should().NotBeNull();

        extractor.AumidCalls.Should().Be(1);
        extractor.PathCalls.Should().Be(0);
    }

    [Fact]
    public void GetOrCreate_PrefersAUserSuppliedIconOverTheTarget()
    {
        using var temp = new TestDirectory();
        var target = temp.File("app.exe");
        var custom = temp.File("custom.png");
        File.WriteAllText(target, "binary");
        File.WriteAllText(custom, "image");

        var extractor = new CountingExtractor();
        var cache = new IconCache(new AppPaths(temp.Path), extractor);

        var shortcut = Shortcut.FromPath(target);
        shortcut.IconPath = custom;

        cache.GetOrCreate(Guid.NewGuid(), shortcut).Should().NotBeNull();
        extractor.PathCalls.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_ReturnsNullWhenNoIconCanBeProduced()
    {
        using var temp = new TestDirectory();
        var source = temp.File("app.exe");
        File.WriteAllText(source, "binary");

        var cache = new IconCache(new AppPaths(temp.Path), new CountingExtractor { ReturnNothing = true });

        cache.GetOrCreate(Guid.NewGuid(), Shortcut.FromPath(source)).Should().BeNull();
    }

    [Fact]
    public void Evict_RemovesTheWholeFolderOfADeletedGroup()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var source = temp.File("app.exe");
        File.WriteAllText(source, "binary");

        var cache = new IconCache(paths, new CountingExtractor());
        var groupId = Guid.NewGuid();
        cache.GetOrCreate(groupId, Shortcut.FromPath(source));

        Directory.Exists(paths.CacheFolderFor(groupId)).Should().BeTrue();

        cache.Evict(groupId);

        Directory.Exists(paths.CacheFolderFor(groupId)).Should().BeFalse();
    }

    [Fact]
    public void Warm_RendersEveryShortcutOfEveryGroup()
    {
        using var temp = new TestDirectory();
        var source = temp.File("app.exe");
        File.WriteAllText(source, "binary");

        var extractor = new CountingExtractor();
        var cache = new IconCache(new AppPaths(temp.Path), extractor);

        var groups = new[]
        {
            new Group { Name = "A", Shortcuts = { Shortcut.FromPath(source) } },
            new Group { Name = "B", Shortcuts = { Shortcut.FromPath(source), Shortcut.FromPath(source) } },
        };

        cache.Warm(groups);

        extractor.PathCalls.Should().Be(3);
    }
}
