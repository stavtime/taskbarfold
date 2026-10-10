using System.Text.Json;
using FluentAssertions;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Serialization;
using TaskbarFold.Core.Services;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class GroupRepositoryTests
{
    private static GroupsDocument SampleDocument()
    {
        var document = new GroupsDocument();
        document.Groups.Add(new Group
        {
            Name = "Работа",
            Columns = 4,
            Background = "#123456",
            Opacity = 0.8,
            Shortcuts =
            {
                new Shortcut
                {
                    DisplayName = "Photoshop",
                    Target = @"C:\Program Files\Adobe\Photoshop.exe",
                    Arguments = "-nosplash",
                    WorkingDirectory = @"C:\Program Files\Adobe",
                    FallbackFileName = "Photoshop.exe",
                    RunAsAdmin = true,
                },
                new Shortcut
                {
                    DisplayName = "Calculator",
                    AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
                },
            },
        });

        return document;
    }

    [Fact]
    public void Load_WithNoFile_ReturnsAnEmptyDocument()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));

        var result = repository.Load();

        result.Status.Should().Be(ConfigLoadStatus.Created);
        result.Document.Groups.Should().BeEmpty();
        result.Document.Version.Should().Be(GroupsDocument.CurrentVersion);
    }

    [Fact]
    public void SaveThenLoad_PreservesEverySignificantField()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));
        var original = SampleDocument();

        repository.Save(original);
        var loaded = repository.Load();

        loaded.Status.Should().Be(ConfigLoadStatus.Loaded);
        loaded.Document.Groups.Should().HaveCount(1);

        var group = loaded.Document.Groups[0];
        group.Id.Should().Be(original.Groups[0].Id);
        group.Name.Should().Be("Работа");
        group.Columns.Should().Be(4);
        group.Background.Should().Be("#123456");
        group.Opacity.Should().BeApproximately(0.8, 0.0001);
        group.Shortcuts.Should().HaveCount(2);

        var desktop = group.Shortcuts[0];
        desktop.Target.Should().Be(@"C:\Program Files\Adobe\Photoshop.exe");
        desktop.Arguments.Should().Be("-nosplash");
        desktop.RunAsAdmin.Should().BeTrue();
        desktop.Kind.Should().Be(ShortcutKind.Desktop);

        var packaged = group.Shortcuts[1];
        packaged.AppUserModelId.Should().Be("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        packaged.Kind.Should().Be(ShortcutKind.Packaged);
    }

    [Fact]
    public void Save_IsAtomic_AndLeavesNoTemporaryFileBehind()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var repository = new GroupRepository(paths);

        repository.Save(SampleDocument());
        repository.Save(SampleDocument());

        File.Exists(paths.ConfigFile).Should().BeTrue();
        Directory.EnumerateFiles(temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Save_KeepsThePreviousVersionAlongsideTheNewOne()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));

        var first = SampleDocument();
        first.Groups[0].Name = "Before";
        repository.Save(first);

        var second = SampleDocument();
        second.Groups[0].Name = "After";
        repository.Save(second);

        // Losing a set of groups to one bad write should never be unrecoverable.
        File.Exists(repository.PreviousConfigFilePath).Should().BeTrue();
        File.ReadAllText(repository.PreviousConfigFilePath).Should().Contain("Before");
        File.ReadAllText(repository.ConfigFilePath).Should().Contain("After");
    }

    [Fact]
    public void Save_OnAFirstRun_LeavesNoBackupBehind()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));

        repository.Save(SampleDocument());

        File.Exists(repository.PreviousConfigFilePath).Should().BeFalse();
    }

    [Fact]
    public void Load_WithMalformedJson_QuarantinesTheFileAndRecovers()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ConfigFile, "{ this is not json");

        var result = new GroupRepository(paths).Load();

        result.Status.Should().Be(ConfigLoadStatus.Recovered);
        result.Document.Groups.Should().BeEmpty();
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();

        // The damaged content has to survive: it may be the user's only copy.
        File.ReadAllText(result.BackupPath!).Should().Contain("this is not json");
    }

    [Fact]
    public void Load_WithAFutureSchemaVersion_RefusesToTouchTheFile()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ConfigFile, """{"version": 99, "groups": []}""");

        var result = new GroupRepository(paths).Load();

        result.Status.Should().Be(ConfigLoadStatus.TooNew);

        // Crucially the original file is still there, unmodified.
        File.ReadAllText(paths.ConfigFile).Should().Contain("99");
    }

    [Fact]
    public void Load_WithAnOlderVersion_MigratesAndReportsIt()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.Root);

        File.WriteAllText(paths.ConfigFile, """
            {
              "version": 0,
              "groups": [
                { "name": "Legacy", "shortcuts": [ { "target": "C:\\Tools\\thing.exe" } ] }
              ]
            }
            """);

        var result = new GroupRepository(paths).Load();

        result.Status.Should().Be(ConfigLoadStatus.Migrated);
        result.Document.Version.Should().Be(GroupsDocument.CurrentVersion);

        var shortcut = result.Document.Groups[0].Shortcuts[0];
        shortcut.Id.Should().NotBe(Guid.Empty);
        shortcut.DisplayName.Should().Be("thing");
        shortcut.FallbackFileName.Should().Be("thing.exe");
    }

    [Fact]
    public void Save_AfterLoadingAFutureSchema_ProtectsTheOriginalAndBackup()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var repository = new GroupRepository(paths);
        const string future = """{"version":99,"groups":[{"name":"Future data"}]}""";
        File.WriteAllText(paths.ConfigFile, future);
        File.WriteAllText(repository.PreviousConfigFilePath, "previous backup");

        var result = repository.Load();
        result.Status.Should().Be(ConfigLoadStatus.TooNew);
        result.Document.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

        Action save = () => repository.Save(result.Document);
        save.Should().Throw<ConfigReadOnlyException>();
        save.Should().Throw<ConfigReadOnlyException>();

        File.ReadAllText(paths.ConfigFile).Should().Be(future);
        File.ReadAllText(repository.PreviousConfigFilePath).Should().Be("previous backup");
        File.Exists(paths.ConfigFile + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Save_AfterLoadingACompatibleReplacement_IsAllowedAgain()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var repository = new GroupRepository(paths);
        File.WriteAllText(paths.ConfigFile, """{"version":99,"groups":[]}""");
        repository.Load();

        File.WriteAllText(paths.ConfigFile, """{"version":1,"groups":[{"name":"Restored"}]}""");
        var result = repository.Load();
        result.Document.Groups[0].Name = "Edited";
        repository.Save(result.Document);

        repository.Load().Document.Groups.Single().Name.Should().Be("Edited");
    }

    [Fact]
    public void Save_RejectsAFutureDocumentWithoutDowngradingIt()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));
        var document = new GroupsDocument { Version = 99 };

        Action save = () => repository.Save(document);

        save.Should().Throw<ConfigReadOnlyException>();
        document.Version.Should().Be(99);
        File.Exists(repository.ConfigFilePath).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"version":1,"groups":[null,{"name":"Keep","shortcuts":[{"displayName":"App","appUserModelId":"Contoso!App"}]},null]}""")]
    [InlineData("""{"version":1,"groups":[{"name":"Keep","shortcuts":[null,{"displayName":"App","appUserModelId":"Contoso!App"},null]}]}""")]
    public void Load_RemovesNullEntriesAndPreservesValidData(string json)
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        var repository = new GroupRepository(paths);
        File.WriteAllText(paths.ConfigFile, json);

        var result = repository.Load();

        result.Status.Should().Be(ConfigLoadStatus.Migrated);
        var group = result.Document.Groups.Should().ContainSingle().Subject;
        group.Name.Should().Be("Keep");
        group.Shortcuts.Should().ContainSingle().Which.AppUserModelId.Should().Be("Contoso!App");

        repository.Save(result.Document);
        var reloaded = repository.Load();
        reloaded.Status.Should().Be(ConfigLoadStatus.Loaded);
        reloaded.Document.Should().BeEquivalentTo(result.Document);
    }

    [Fact]
    public void SerializedJson_UsesCamelCaseAndOmitsDerivedMembers()
    {
        var json = JsonSerializer.Serialize(SampleDocument(), ConfigJsonContext.Default.GroupsDocument);

        json.Should().Contain("\"displayName\"");
        json.Should().Contain("\"appUserModelId\"");

        // AppUserModelId on the group is derived from its id; writing it would create a
        // second source of truth that could drift from the GUID.
        json.Should().NotContain("TaskbarFold.Group.");

        // Nulls are dropped so a hand-edited config stays readable.
        json.Should().NotContain("\"arguments\": null");
    }

    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.Root);

        File.WriteAllText(paths.ConfigFile, """
            {
              // A user may reasonably annotate this file by hand.
              "version": 1,
              "groups": [],
            }
            """);

        var result = new GroupRepository(paths).Load();

        result.Status.Should().BeOneOf(ConfigLoadStatus.Loaded, ConfigLoadStatus.Migrated);
    }
}
