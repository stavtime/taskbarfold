using FluentAssertions;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Services;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class ConfigMigratorTests
{
    [Fact]
    public void Migrate_OnACurrentDocument_ChangesNothing()
    {
        var document = new GroupsDocument();
        document.Groups.Add(new Group { Name = "Design" });

        ConfigMigrator.Migrate(document).Should().Be(MigrationOutcome.UpToDate);
    }

    [Fact]
    public void Migrate_RejectsANewerSchema()
    {
        var document = new GroupsDocument { Version = GroupsDocument.CurrentVersion + 1 };

        ConfigMigrator.Migrate(document).Should().Be(MigrationOutcome.TooNew);
    }

    [Fact]
    public void Migrate_AssignsIdsToEntriesThatHaveNone()
    {
        var document = new GroupsDocument { Version = 0 };
        document.Groups.Add(new Group
        {
            Id = Guid.Empty,
            Name = "Games",
            Shortcuts = { new Shortcut { Id = Guid.Empty, Target = @"C:\Games\game.exe" } },
        });

        ConfigMigrator.Migrate(document).Should().Be(MigrationOutcome.Upgraded);

        document.Groups[0].Id.Should().NotBe(Guid.Empty);
        document.Groups[0].Shortcuts[0].Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Migrate_BreaksTiesBetweenDuplicateIds()
    {
        // Duplicated ids are what happens when a user copies a group by editing the JSON.
        // Two groups sharing an id would share an AUMID, and Windows would merge their
        // taskbar buttons into one.
        var shared = Guid.NewGuid();
        var document = new GroupsDocument
        {
            Groups =
            {
                new Group { Id = shared, Name = "One" },
                new Group { Id = shared, Name = "Two" },
            },
        };

        ConfigMigrator.Migrate(document).Should().Be(MigrationOutcome.Upgraded);

        document.Groups[0].Id.Should().NotBe(document.Groups[1].Id);
    }

    [Fact]
    public void Migrate_FillsTheRelocationHintFromTheTarget()
    {
        var document = new GroupsDocument
        {
            Groups = { new Group { Shortcuts = { new Shortcut { Target = @"D:\Apps\editor.exe" } } } },
        };

        ConfigMigrator.Migrate(document);

        document.Groups[0].Shortcuts[0].FallbackFileName.Should().Be("editor.exe");
    }

    [Fact]
    public void Migrate_NamesAPackagedShortcutAfterItsApplicationId()
    {
        var document = new GroupsDocument
        {
            Groups = { new Group { Shortcuts = { new Shortcut { AppUserModelId = "Contoso_abc!App" } } } },
        };

        ConfigMigrator.Migrate(document);

        document.Groups[0].Shortcuts[0].DisplayName.Should().Be("Contoso_abc!App");
    }
}
