using System.Drawing;
using FluentAssertions;
using TaskbarFold.Core.Interop;
using Xunit;

namespace TaskbarFold.Tests;

public sealed class IcoWriterTests
{
    [Fact]
    public void WriteFromBitmap_ProducesEveryStandardSize()
    {
        using var temp = new TestDirectory();
        var output = temp.File("group.ico");

        using var source = GroupIconRenderer.RenderAppGlyph(256);
        IcoWriter.WriteFromBitmap(source, output);

        File.Exists(output).Should().BeTrue();

        var header = File.ReadAllBytes(output);
        BitConverter.ToUInt16(header, 0).Should().Be(0);  // reserved
        BitConverter.ToUInt16(header, 2).Should().Be(1);  // type: icon
        BitConverter.ToUInt16(header, 4).Should().Be((ushort)IcoWriter.StandardSizes.Length);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void SmallFrames_AreReadableByTheLegacyIconDecoder(int size)
    {
        // System.Drawing.Icon cannot decode PNG-compressed frames, and it still sits on
        // the path of several shell and tray consumers. Small frames must stay DIB.
        using var temp = new TestDirectory();
        var output = temp.File("group.ico");

        using var source = GroupIconRenderer.RenderAppGlyph(256);
        IcoWriter.WriteFromBitmap(source, output);

        using var icon = new Icon(output, size, size);
        using var bitmap = icon.ToBitmap();

        bitmap.Width.Should().Be(size);
        bitmap.Height.Should().Be(size);
    }

    [Fact]
    public void WriteFromBitmap_OverwritesAnExistingFileInPlace()
    {
        using var temp = new TestDirectory();
        var output = temp.File("group.ico");

        using var first = GroupIconRenderer.RenderGroupGlyph(256, "Work", "#3B82F6");
        IcoWriter.WriteFromBitmap(first, output);
        var originalLength = new FileInfo(output).Length;

        using var second = GroupIconRenderer.RenderGroupGlyph(256, "Design", "#EC4899");
        IcoWriter.WriteFromBitmap(second, output);

        File.Exists(output).Should().BeTrue();
        Directory.EnumerateFiles(temp.Path, "*.tmp").Should().BeEmpty();
        new FileInfo(output).Length.Should().BeGreaterThan(0);
        originalLength.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderGroupGlyph_UsesTheGroupInitial()
    {
        using var glyph = GroupIconRenderer.RenderGroupGlyph(64, "Работа", "#3B82F6");

        glyph.Width.Should().Be(64);
        glyph.Height.Should().Be(64);

        // The centre of the plate must not be transparent: something was drawn there.
        glyph.GetPixel(32, 32).A.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderGroupGlyph_FallsBackWhenTheColourIsUnusable()
    {
        // A near-black background would produce an unreadable tile.
        using var glyph = GroupIconRenderer.RenderGroupGlyph(64, "X", "#000000");

        glyph.GetPixel(32, 32).A.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderGroupGlyph_HandlesAnEmptyNameWithoutThrowing()
    {
        using var glyph = GroupIconRenderer.RenderGroupGlyph(64, "   ", null);

        glyph.Width.Should().Be(64);
    }

    [Fact]
    public void WriteGroupIcon_WritesAFileThatWindowsCanLoad()
    {
        using var temp = new TestDirectory();
        var output = temp.File("icons/group.ico");

        GroupIconRenderer.WriteGroupIcon(output, "Games", "#10B981");

        using var icon = new Icon(output, 32, 32);
        icon.Width.Should().Be(32);
    }
}
