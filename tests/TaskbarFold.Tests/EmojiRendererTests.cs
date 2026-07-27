using System.Drawing;
using FluentAssertions;
using TaskbarFold.Core.Interop;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Serialization;
using TaskbarFold.Core.Services;
using Xunit;
using System.Text.Json;

namespace TaskbarFold.Tests;

public sealed class EmojiRendererTests
{
    private const string Controller = "\U0001F3AE";
    private const string ThumbsUpMedium = "\U0001F44D\U0001F3FD";
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

    /// <summary>Share of visibly coloured pixels; a monochrome silhouette scores ~0.</summary>
    private static double ColourShare(Bitmap bitmap)
    {
        int coloured = 0, opaque = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.A < 8)
                {
                    continue;
                }

                opaque++;
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                if (max - min > 24)
                {
                    coloured++;
                }
            }
        }

        return opaque == 0 ? 0 : (double)coloured / opaque;
    }

    [Fact]
    public void Render_ProducesColour_NotASilhouette()
    {
        // The entire reason this goes through Direct2D: GDI+ and WPF both render emoji
        // as flat black shapes, which would be worthless as an icon.
        if (!EmojiRenderer.IsSupported)
        {
            return;
        }

        using var bitmap = EmojiRenderer.Render(Controller, 128);

        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(128);
        ColourShare(bitmap).Should().BeGreaterThan(0.5);
    }

    [Theory]
    [InlineData(Controller)]
    [InlineData(ThumbsUpMedium)]
    [InlineData(Family)]
    public void Render_HandlesMultiCodepointSequences(string emoji)
    {
        // Skin-tone modifiers and ZWJ sequences must shape as one glyph rather than
        // decomposing into their parts.
        if (!EmojiRenderer.IsSupported)
        {
            return;
        }

        using var bitmap = EmojiRenderer.Render(emoji, 96);

        bitmap.Should().NotBeNull();
        ColourShare(bitmap!).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void RenderMany_ReturnsAnEntryPerRequestedEmoji()
    {
        if (!EmojiRenderer.IsSupported)
        {
            return;
        }

        var rendered = EmojiRenderer.RenderMany([Controller, "\U0001F680", "⭐"], 32);

        rendered.Should().HaveCount(3);
        rendered.Values.Should().OnlyContain(png => png.Length > 0);

        // PNG signature.
        rendered[Controller].Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
    }

    [Theory]
    [InlineData(Controller, true)]
    [InlineData("⭐", true)]
    [InlineData("A", false)]
    [InlineData("Work", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeEmoji_DistinguishesSymbolsFromPlainText(string? text, bool expected)
    {
        EmojiRenderer.LooksLikeEmoji(text).Should().Be(expected);
    }

    [Fact]
    public void FirstEmoji_KeepsAZwjSequenceIntact()
    {
        // Naively taking one char would slice the family into a lone man emoji.
        EmojiRenderer.FirstEmoji(Family).Should().Be(Family);
    }

    [Fact]
    public void FirstEmoji_TakesOnlyTheFirstOfSeveral()
    {
        EmojiRenderer.FirstEmoji(Controller + "\U0001F680").Should().Be(Controller);
    }

    [Fact]
    public void RenderGroupGlyph_DrawsTheEmojiOverTheGroupColour()
    {
        if (!EmojiRenderer.IsSupported)
        {
            return;
        }

        using var withEmoji = GroupIconRenderer.RenderGroupGlyph(128, "Games", "#3B82F6", Controller);
        using var withInitial = GroupIconRenderer.RenderGroupGlyph(128, "Games", "#3B82F6");

        // Both are colourful because of the plate, so compare the centre, where the
        // emoji sits and the initial would otherwise be near-white.
        var centre = withEmoji.GetPixel(64, 64);
        var plainCentre = withInitial.GetPixel(64, 64);

        centre.Should().NotBe(plainCentre);
        withEmoji.Width.Should().Be(128);
    }

    [Fact]
    public void RenderGroupGlyph_FallsBackToTheInitialWhenTheEmojiIsJustText()
    {
        using var fromText = GroupIconRenderer.RenderGroupGlyph(96, "Work", "#3B82F6", "W");
        using var fromNothing = GroupIconRenderer.RenderGroupGlyph(96, "Work", "#3B82F6");

        // "W" is not an emoji, so both paths must produce the same initial-based tile.
        fromText.GetPixel(48, 48).Should().Be(fromNothing.GetPixel(48, 48));
    }

    [Fact]
    public void WriteGroupIcon_WithAnEmoji_ProducesALoadableIcon()
    {
        using var temp = new TestDirectory();
        var output = temp.File("group.ico");

        GroupIconRenderer.WriteGroupIcon(output, "Games", "#10B981", Controller);

        using var icon = new Icon(output, 32, 32);
        icon.Width.Should().Be(32);
    }

    [Fact]
    public void RenderGroupTilePng_ReturnsDecodablePngBytes()
    {
        var png = GroupIconRenderer.RenderGroupTilePng(64, "Games", "#3B82F6", Controller);

        png.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');

        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        bitmap.Width.Should().Be(64);
    }

    [Fact]
    public void Emoji_SurvivesASaveLoadRoundTrip()
    {
        using var temp = new TestDirectory();
        var repository = new GroupRepository(new AppPaths(temp.Path));

        var document = new GroupsDocument();
        document.Groups.Add(new Group { Name = "Games", Emoji = Controller });
        repository.Save(document);

        var loaded = repository.Load();

        loaded.Document.Groups[0].Emoji.Should().Be(Controller);
    }

    [Fact]
    public void Emoji_IsOmittedFromJsonWhenUnset()
    {
        var document = new GroupsDocument();
        document.Groups.Add(new Group { Name = "Work" });

        var json = JsonSerializer.Serialize(document, ConfigJsonContext.Default.GroupsDocument);

        json.Should().NotContain("emoji");
    }

    [Fact]
    public void Clone_CarriesTheEmojiToTheCopy()
    {
        var original = new Group { Name = "Games", Emoji = Controller };

        original.Clone().Emoji.Should().Be(Controller);
    }

    [Fact]
    public void ConfigWithAnEmoji_LoadsFromHandWrittenJson()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Path);
        Directory.CreateDirectory(paths.Root);

        File.WriteAllText(paths.ConfigFile, $$"""
            { "version": 1, "groups": [ { "name": "Games", "emoji": "{{Controller}}" } ] }
            """);

        var result = new GroupRepository(paths).Load();

        result.Document.Groups[0].Emoji.Should().Be(Controller);
    }
}
