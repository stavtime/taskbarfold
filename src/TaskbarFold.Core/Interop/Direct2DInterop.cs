using System.Runtime.InteropServices;

namespace TaskbarFold.Core.Interop;

// COM declarations for the Direct2D / DirectWrite / WIC pipeline used to rasterise
// colour emoji. Neither GDI+ nor WPF supports COLR/CPAL colour fonts, so an emoji drawn
// through either comes out as a black silhouette; Direct2D's DrawText with
// D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT is the supported way to get the real thing.
//
// Methods these interfaces do not use are declared as empty slots. A ComImport interface
// is bound by vtable position, so every preceding method must occupy its slot, but its
// signature is irrelevant as long as it is never invoked.

[StructLayout(LayoutKind.Sequential)]
internal struct D2D1_PIXEL_FORMAT
{
    public uint format;
    public uint alphaMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D1_RENDER_TARGET_PROPERTIES
{
    public uint type;
    public D2D1_PIXEL_FORMAT pixelFormat;
    public float dpiX;
    public float dpiY;
    public uint usage;
    public uint minLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D1_COLOR_F
{
    public float r;
    public float g;
    public float b;
    public float a;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D1_RECT_F
{
    public float left;
    public float top;
    public float right;
    public float bottom;
}

internal static class D2DConstants
{
    internal const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    internal const uint D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
    internal const uint D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
    internal const uint DWRITE_FACTORY_TYPE_SHARED = 0;

    /// <summary>D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT — the whole point of this path.</summary>
    internal const uint D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 4;

    internal const uint DWRITE_FONT_WEIGHT_NORMAL = 400;
    internal const uint DWRITE_FONT_STYLE_NORMAL = 0;
    internal const uint DWRITE_FONT_STRETCH_NORMAL = 5;
    internal const uint DWRITE_TEXT_ALIGNMENT_CENTER = 2;
    internal const uint DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2;

    internal const uint WICBitmapCacheOnLoad = 2;

    internal static readonly Guid CLSID_WICImagingFactory = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    internal static readonly Guid IID_IWICImagingFactory = new("EC5EC8A9-C395-4314-9C77-54D7A935FF70");
    internal static readonly Guid IID_ID2D1Factory = new("06152247-6F50-465A-9245-118BFD3B6007");
    internal static readonly Guid IID_IDWriteFactory = new("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");

    /// <summary>GUID_WICPixelFormat32bppPBGRA — premultiplied BGRA, matching Direct2D.</summary>
    internal static readonly Guid GUID_WICPixelFormat32bppPBGRA = new("6FDDC324-4E03-4BFE-B185-3D77768DC910");

    [DllImport("d2d1.dll", PreserveSig = false)]
    internal static extern void D2D1CreateFactory(
        uint factoryType,
        in Guid riid,
        nint factoryOptions,
        [MarshalAs(UnmanagedType.Interface)] out object factory);

    [DllImport("dwrite.dll", PreserveSig = false)]
    internal static extern void DWriteCreateFactory(
        uint factoryType,
        in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object factory);
}

[ComImport]
[Guid("EC5EC8A9-C395-4314-9C77-54D7A935FF70")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICImagingFactory
{
    void Slot00_CreateDecoderFromFilename();
    void Slot01_CreateDecoderFromStream();
    void Slot02_CreateDecoderFromFileHandle();
    void Slot03_CreateComponentInfo();
    void Slot04_CreateDecoder();
    void Slot05_CreateEncoder();
    void Slot06_CreatePalette();
    void Slot07_CreateFormatConverter();
    void Slot08_CreateBitmapScaler();
    void Slot09_CreateBitmapClipper();
    void Slot10_CreateBitmapFlipRotator();
    void Slot11_CreateStream();
    void Slot12_CreateColorContext();
    void Slot13_CreateColorTransformer();

    void CreateBitmap(uint width, uint height, in Guid pixelFormat, uint option, out IWICBitmap bitmap);
}

[ComImport]
[Guid("00000121-A8F2-4877-BA0A-FD2B6645FB94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmap
{
    // IWICBitmapSource
    void GetSize(out uint width, out uint height);

    void Slot01_GetPixelFormat();
    void Slot02_GetResolution();
    void Slot03_CopyPalette();

    // The buffer is a raw pointer rather than a byte[]: array parameters on a ComImport
    // interface default to SAFEARRAY marshalling, which corrupts the call.
    void CopyPixels(nint rectangle, uint stride, uint bufferSize, nint buffer);

    // IWICBitmap
    void Slot05_Lock();
    void Slot06_SetPalette();
    void Slot07_SetResolution();
}

[ComImport]
[Guid("06152247-6F50-465A-9245-118BFD3B6007")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID2D1Factory
{
    void Slot00_ReloadSystemMetrics();
    void Slot01_GetDesktopDpi();
    void Slot02_CreateRectangleGeometry();
    void Slot03_CreateRoundedRectangleGeometry();
    void Slot04_CreateEllipseGeometry();
    void Slot05_CreateGeometryGroup();
    void Slot06_CreateTransformedGeometry();
    void Slot07_CreatePathGeometry();
    void Slot08_CreateStrokeStyle();
    void Slot09_CreateDrawingStateBlock();

    void CreateWicBitmapRenderTarget(
        IWICBitmap target,
        in D2D1_RENDER_TARGET_PROPERTIES renderTargetProperties,
        out ID2D1RenderTarget renderTarget);
}

[ComImport]
[Guid("2CD90694-12E2-11DC-9FED-001143A055F9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID2D1RenderTarget
{
    // ID2D1Resource
    void Slot00_GetFactory();

    void Slot01_CreateBitmap();
    void Slot02_CreateBitmapFromWicBitmap();
    void Slot03_CreateSharedBitmap();
    void Slot04_CreateBitmapBrush();

    void CreateSolidColorBrush(
        in D2D1_COLOR_F color, nint brushProperties, out ID2D1SolidColorBrush brush);

    void Slot06_CreateGradientStopCollection();
    void Slot07_CreateLinearGradientBrush();
    void Slot08_CreateRadialGradientBrush();
    void Slot09_CreateCompatibleRenderTarget();
    void Slot10_CreateLayer();
    void Slot11_CreateMesh();
    void Slot12_DrawLine();
    void Slot13_DrawRectangle();
    void Slot14_FillRectangle();
    void Slot15_DrawRoundedRectangle();
    void Slot16_FillRoundedRectangle();
    void Slot17_DrawEllipse();
    void Slot18_FillEllipse();
    void Slot19_DrawGeometry();
    void Slot20_FillGeometry();
    void Slot21_FillMesh();
    void Slot22_FillOpacityMask();
    void Slot23_DrawBitmap();

    void DrawText(
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        uint textLength,
        IDWriteTextFormat textFormat,
        in D2D1_RECT_F layoutRect,
        ID2D1SolidColorBrush defaultForegroundBrush,
        uint options,
        uint measuringMode);

    void Slot25_DrawTextLayout();
    void Slot26_DrawGlyphRun();
    void Slot27_SetTransform();
    void Slot28_GetTransform();
    void Slot29_SetAntialiasMode();
    void Slot30_GetAntialiasMode();
    void Slot31_SetTextAntialiasMode();
    void Slot32_GetTextAntialiasMode();
    void Slot33_SetTextRenderingParams();
    void Slot34_GetTextRenderingParams();
    void Slot35_SetTags();
    void Slot36_GetTags();
    void Slot37_PushLayer();
    void Slot38_PopLayer();
    void Slot39_Flush();
    void Slot40_SaveDrawingState();
    void Slot41_RestoreDrawingState();
    void Slot42_PushAxisAlignedClip();
    void Slot43_PopAxisAlignedClip();

    void Clear(in D2D1_COLOR_F color);

    void BeginDraw();

    [PreserveSig]
    int EndDraw(out ulong tag1, out ulong tag2);
}

[ComImport]
[Guid("2CD906A9-12E2-11DC-9FED-001143A055F9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID2D1SolidColorBrush
{
    void Slot00_GetFactory();
    void Slot01_SetOpacity();
    void Slot02_GetOpacity();
    void Slot03_SetTransform();
    void Slot04_GetTransform();
    void Slot05_SetColor();
    void Slot06_GetColor();
}

[ComImport]
[Guid("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDWriteFactory
{
    void Slot00_GetSystemFontCollection();
    void Slot01_CreateCustomFontCollection();
    void Slot02_RegisterFontCollectionLoader();
    void Slot03_UnregisterFontCollectionLoader();
    void Slot04_CreateFontFileReference();
    void Slot05_CreateCustomFontFileReference();
    void Slot06_CreateFontFace();
    void Slot07_CreateRenderingParams();
    void Slot08_CreateMonitorRenderingParams();
    void Slot09_CreateCustomRenderingParams();
    void Slot10_RegisterFontFileLoader();
    void Slot11_UnregisterFontFileLoader();

    void CreateTextFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string fontFamilyName,
        nint fontCollection,
        uint fontWeight,
        uint fontStyle,
        uint fontStretch,
        float fontSize,
        [MarshalAs(UnmanagedType.LPWStr)] string localeName,
        out IDWriteTextFormat textFormat);
}

[ComImport]
[Guid("9C906818-31D7-4FD3-A151-7C5E225DB55A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDWriteTextFormat
{
    void SetTextAlignment(uint textAlignment);

    void SetParagraphAlignment(uint paragraphAlignment);
}
