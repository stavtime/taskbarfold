using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace TaskbarFold.Core.Interop;

/// <summary>
/// Draws the tile used for a group that has no custom icon: a rounded plate in the
/// group's colour carrying either its initial or the four-tile app-folder glyph.
///
/// Groups need a real icon rather than a generic one because the whole point is to
/// tell four or five pinned buttons apart at a glance.
/// </summary>
public static class GroupIconRenderer
{
    /// <summary>Palette used when a group has no explicit background colour.</summary>
    public static readonly string[] SuggestedColors =
    [
        "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444",
        "#F59E0B", "#10B981", "#14B8A6", "#64748B",
    ];

    /// <summary>Renders the four-tile glyph — the product mark, used for the app itself.</summary>
    public static Bitmap RenderAppGlyph(int size)
    {
        var bitmap = NewCanvas(size, out var graphics);
        using (graphics)
        {
            var plate = DrawPlate(graphics, size, ColorTranslator.FromHtml("#408CF5"), ColorTranslator.FromHtml("#1A44A0"));
            plate.Dispose();
            DrawTiles(graphics, size);
        }

        return bitmap;
    }

    /// <summary>
    /// Renders a group tile: the group's colour plus, in order of preference, the chosen
    /// emoji, the group's first character, or the generic four-tile mark.
    /// </summary>
    public static Bitmap RenderGroupGlyph(int size, string groupName, string? backgroundHex, string? emoji = null)
    {
        var baseColor = ParseColor(backgroundHex) ?? PickColor(groupName);
        var bitmap = NewCanvas(size, out var graphics);

        using (graphics)
        {
            var plate = DrawPlate(graphics, size, Lighten(baseColor, 0.18), Darken(baseColor, 0.22));
            plate.Dispose();

            if (TryDrawEmoji(graphics, size, emoji))
            {
                return bitmap;
            }

            var initial = Initial(groupName);
            if (initial is null)
            {
                DrawTiles(graphics, size);
                return bitmap;
            }

            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Segoe UI Semibold at roughly half the tile keeps one glyph optically centred.
            using var font = new Font("Segoe UI Semibold", size * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(246, 255, 255, 255));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            graphics.DrawString(initial, font, brush, new RectangleF(0, 0, size, size), format);
        }

        return bitmap;
    }

    /// <summary>Renders the group glyph at every standard size and writes it as an .ico.</summary>
    public static void WriteGroupIcon(
        string destinationIcoPath, string groupName, string? backgroundHex, string? emoji = null)
    {
        using var source = RenderGroupGlyph(256, groupName, backgroundHex, emoji);
        IcoWriter.WriteFromBitmap(source, destinationIcoPath);
    }

    /// <summary>
    /// Renders the tile as PNG bytes, for a UI layer that wants to show exactly what will
    /// end up on the taskbar rather than an approximation drawn with its own text stack.
    /// </summary>
    public static byte[] RenderGroupTilePng(int size, string groupName, string? backgroundHex, string? emoji = null)
    {
        using var bitmap = RenderGroupGlyph(size, groupName, backgroundHex, emoji);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Draws the emoji centred on the plate. Returns false when there is nothing usable
    /// to draw, or when the system cannot rasterise colour fonts — the caller then falls
    /// back to the initial rather than stamping a black silhouette on the tile.
    /// </summary>
    private static bool TryDrawEmoji(Graphics graphics, int size, string? emoji)
    {
        var glyph = EmojiRenderer.FirstEmoji(emoji);
        if (glyph is null || !EmojiRenderer.LooksLikeEmoji(glyph))
        {
            return false;
        }

        // Small tiles give the glyph proportionally more room. At 16 px the plate margin
        // would otherwise leave about ten pixels of emoji, which reads as a smudge.
        var fill = size <= 24 ? 0.86 : size <= 48 ? 0.74 : 0.62;
        var target = (int)Math.Round(size * fill);
        using var rendered = EmojiRenderer.Render(glyph, Math.Max(target * 2, 32));
        if (rendered is null)
        {
            return false;
        }

        var offset = (size - target) / 2f;
        graphics.DrawImage(rendered, offset, offset, target, target);
        return true;
    }

    private static Bitmap NewCanvas(int size, out Graphics graphics)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        return bitmap;
    }

    private static GraphicsPath DrawPlate(Graphics graphics, int size, Color from, Color to)
    {
        var padding = size * 0.055f;
        var side = size - (2 * padding);
        var path = RoundedRect(padding, padding, side, side, size * 0.22f);

        using var brush = new LinearGradientBrush(
            new RectangleF(padding, padding, side, side), from, to, LinearGradientMode.ForwardDiagonal);
        graphics.FillPath(brush, path);
        return path;
    }

    private static void DrawTiles(Graphics graphics, int size)
    {
        var tile = size * 0.195f;
        var gap = size * 0.085f;
        var block = (2 * tile) + gap;
        var originX = (size - block) / 2;
        var originY = (size - block) / 2;
        var radius = Math.Max(0.75f, tile * 0.30f);

        using var bright = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        using var dim = new SolidBrush(Color.FromArgb(160, 255, 255, 255));

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                using var path = RoundedRect(
                    originX + (column * (tile + gap)), originY + (row * (tile + gap)), tile, tile, radius);
                graphics.FillPath(row == 1 && column == 1 ? dim : bright, path);
            }
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(width, height));

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string? Initial(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Take a full text element so an emoji or a surrogate pair is not cut in half.
        var enumerator = StringInfo.GetTextElementEnumerator(trimmed);
        if (!enumerator.MoveNext())
        {
            return null;
        }

        var element = enumerator.GetTextElement();
        return element.Length == 1 ? element.ToUpperInvariant() : element;
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            var color = ColorTranslator.FromHtml(hex);

            // A near-black background produces an unreadable tile; fall back to the palette.
            var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            return luminance < 24 ? null : Color.FromArgb(255, color.R, color.G, color.B);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static Color PickColor(string name)
    {
        var hash = 0;
        foreach (var c in name)
        {
            hash = unchecked((hash * 31) + c);
        }

        var index = Math.Abs(hash % SuggestedColors.Length);
        return ColorTranslator.FromHtml(SuggestedColors[index]);
    }

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        255,
        (int)Math.Clamp(color.R + ((255 - color.R) * amount), 0, 255),
        (int)Math.Clamp(color.G + ((255 - color.G) * amount), 0, 255),
        (int)Math.Clamp(color.B + ((255 - color.B) * amount), 0, 255));

    private static Color Darken(Color color, double amount) => Color.FromArgb(
        255,
        (int)Math.Clamp(color.R * (1 - amount), 0, 255),
        (int)Math.Clamp(color.G * (1 - amount), 0, 255),
        (int)Math.Clamp(color.B * (1 - amount), 0, 255));
}
