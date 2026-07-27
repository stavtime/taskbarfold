using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

/// <summary>
/// Rasterises emoji into bitmaps, in colour.
///
/// This goes through Direct2D rather than GDI+ or WPF because neither of those supports
/// COLR/CPAL colour fonts: an emoji drawn with <c>Graphics.DrawString</c> or WPF's
/// <c>FormattedText</c> comes out as a flat black silhouette, which is useless as an
/// icon. Direct2D's <c>DrawText</c> with <c>ENABLE_COLOR_FONT</c> composites the real
/// glyph layers.
///
/// Text is drawn as a string rather than as a glyph run so that multi-codepoint emoji —
/// skin-tone modifiers, ZWJ sequences such as family emoji, and flags — shape correctly.
/// </summary>
public static class EmojiRenderer
{
    private const string EmojiFontFamily = "Segoe UI Emoji";

    private static readonly object Gate = new();
    private static bool _probed;
    private static bool _supported;

    /// <summary>
    /// Whether colour emoji rendering is available. False on systems missing Direct2D or
    /// the emoji font, in which case callers fall back to the group's initial.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            lock (Gate)
            {
                if (!_probed)
                {
                    _probed = true;
                    _supported = Probe();
                }

                return _supported;
            }
        }
    }

    /// <summary>
    /// Returns true when the text is worth treating as an emoji: it must contain at
    /// least one non-Latin symbol. A plain letter renders better through the initial path.
    /// </summary>
    public static bool LooksLikeEmoji(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value > 0x24F)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes the first complete emoji from the input, keeping modifiers and ZWJ
    /// sequences intact so a family or skin-toned emoji is not sliced in half.
    /// </summary>
    public static string? FirstEmoji(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var enumerator = StringInfo.GetTextElementEnumerator(trimmed);
        return enumerator.MoveNext() ? enumerator.GetTextElement() : null;
    }

    /// <summary>
    /// Renders one emoji centred on a transparent square.
    /// Returns null when Direct2D is unavailable or the draw fails.
    /// </summary>
    public static Bitmap? Render(string emoji, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emoji);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        if (!IsSupported)
        {
            return null;
        }

        using var session = Session.TryCreate();
        return session?.Render(emoji, size);
    }

    /// <summary>
    /// Renders a batch as PNG bytes, reusing one set of Direct2D factories.
    ///
    /// Building the factories costs far more than drawing a glyph, so a picker with a
    /// few dozen entries must not pay for them once per emoji.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> RenderMany(IEnumerable<string> emoji, int size)
    {
        ArgumentNullException.ThrowIfNull(emoji);

        var results = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!IsSupported)
        {
            return results;
        }

        using var session = Session.TryCreate();
        if (session is null)
        {
            return results;
        }

        foreach (var item in emoji.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            using var bitmap = session.Render(item, size);
            if (bitmap is null)
            {
                continue;
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            results[item] = stream.ToArray();
        }

        return results;
    }

    private static bool Probe()
    {
        try
        {
            using var session = Session.TryCreate();
            using var probe = session?.Render("✅", 16);
            return probe is not null;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or InvalidCastException
                                      or EntryPointNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// One set of Direct2D, DirectWrite and WIC factories, held open across several draws.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private readonly object _wicFactoryObject;
        private readonly object _d2dFactoryObject;
        private readonly object _dwriteFactoryObject;
        private readonly IWICImagingFactory _wic;
        private readonly ID2D1Factory _d2d;
        private readonly IDWriteFactory _dwrite;

        private Session(
            object wicFactoryObject, object d2dFactoryObject, object dwriteFactoryObject,
            IWICImagingFactory wic, ID2D1Factory d2d, IDWriteFactory dwrite)
        {
            _wicFactoryObject = wicFactoryObject;
            _d2dFactoryObject = d2dFactoryObject;
            _dwriteFactoryObject = dwriteFactoryObject;
            _wic = wic;
            _d2d = d2d;
            _dwrite = dwrite;
        }

        internal static Session? TryCreate()
        {
            object? wicObject = null;
            object? d2dObject = null;
            object? dwriteObject = null;

            try
            {
                var wicType = Type.GetTypeFromCLSID(D2DConstants.CLSID_WICImagingFactory);
                if (wicType is null)
                {
                    return null;
                }

                wicObject = Activator.CreateInstance(wicType);

                D2DConstants.D2D1CreateFactory(
                    D2DConstants.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                    D2DConstants.IID_ID2D1Factory,
                    nint.Zero,
                    out d2dObject);

                D2DConstants.DWriteCreateFactory(
                    D2DConstants.DWRITE_FACTORY_TYPE_SHARED,
                    D2DConstants.IID_IDWriteFactory,
                    out dwriteObject);

                if (wicObject is IWICImagingFactory wic &&
                    d2dObject is ID2D1Factory d2d &&
                    dwriteObject is IDWriteFactory dwrite)
                {
                    return new Session(wicObject, d2dObject, dwriteObject, wic, d2d, dwrite);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException
                                          or EntryPointNotFoundException or NotSupportedException)
            {
                // Falls through to the cleanup below.
            }

            Release(dwriteObject);
            Release(d2dObject);
            Release(wicObject);
            return null;
        }

        internal Bitmap? Render(string emoji, int size)
        {
            IWICBitmap? wicBitmap = null;
            ID2D1RenderTarget? renderTarget = null;
            ID2D1SolidColorBrush? brush = null;
            IDWriteTextFormat? textFormat = null;

            try
            {
                var pixelFormat = D2DConstants.GUID_WICPixelFormat32bppPBGRA;
                _wic.CreateBitmap(
                    (uint)size, (uint)size, in pixelFormat, D2DConstants.WICBitmapCacheOnLoad, out wicBitmap);

                var properties = new D2D1_RENDER_TARGET_PROPERTIES
                {
                    type = 0,
                    pixelFormat = new D2D1_PIXEL_FORMAT
                    {
                        format = D2DConstants.DXGI_FORMAT_B8G8R8A8_UNORM,
                        alphaMode = D2DConstants.D2D1_ALPHA_MODE_PREMULTIPLIED,
                    },
                    dpiX = 96,
                    dpiY = 96,
                    usage = 0,
                    minLevel = 0,
                };

                _d2d.CreateWicBitmapRenderTarget(wicBitmap, in properties, out renderTarget);

                // Emoji are roughly square; a font size just under the tile leaves margin
                // for glyphs whose ink extends past the nominal em box.
                _dwrite.CreateTextFormat(
                    EmojiFontFamily,
                    nint.Zero,
                    D2DConstants.DWRITE_FONT_WEIGHT_NORMAL,
                    D2DConstants.DWRITE_FONT_STYLE_NORMAL,
                    D2DConstants.DWRITE_FONT_STRETCH_NORMAL,
                    size * 0.78f,
                    "en-us",
                    out textFormat);

                textFormat.SetTextAlignment(D2DConstants.DWRITE_TEXT_ALIGNMENT_CENTER);
                textFormat.SetParagraphAlignment(D2DConstants.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

                var black = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 1 };
                renderTarget.CreateSolidColorBrush(in black, nint.Zero, out brush);

                var transparent = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 };
                var layout = new D2D1_RECT_F { left = 0, top = 0, right = size, bottom = size };

                renderTarget.BeginDraw();
                renderTarget.Clear(in transparent);
                renderTarget.DrawText(
                    emoji,
                    (uint)emoji.Length,
                    textFormat,
                    in layout,
                    brush,
                    D2DConstants.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT,
                    0);

                return renderTarget.EndDraw(out _, out _) < 0 ? null : CopyToBitmap(wicBitmap, size);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                return null;
            }
            finally
            {
                Release(textFormat);
                Release(brush);
                Release(renderTarget);
                Release(wicBitmap);
            }
        }

        public void Dispose()
        {
            Release(_dwriteFactoryObject);
            Release(_d2dFactoryObject);
            Release(_wicFactoryObject);
        }
    }

    /// <summary>
    /// Copies the WIC surface straight into a GDI+ bitmap. Both sides are premultiplied
    /// BGRA, so WIC writes directly into the locked scan lines — no intermediate buffer
    /// and no format conversion.
    /// </summary>
    private static Bitmap CopyToBitmap(IWICBitmap source, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            source.CopyPixels(nint.Zero, (uint)data.Stride, (uint)(data.Stride * size), data.Scan0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
