using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media;
using static QuotaLens.Mac.MacStatusItem;

namespace QuotaLens.Mac.Rendering;

/// <summary>
/// Draws the menu bar thumbnail with AppKit into a bitmap, so it uses the real system font the way
/// Apple's own menu bar items do: SF semibold for the letters and SF medium with equal-width digits,
/// like the menu bar clock, for the numbers. Skia cannot load the system font by name because it is a
/// hidden family. AppKit's origin is bottom-left; the canvas API is top-left, so y is flipped here.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class AppKitThumbnailCanvas : IThumbnailCanvas
{
    private const string ObjC = "/usr/lib/libobjc.dylib";
    private const double SemiboldWeight = 0.3; // NSFontWeightSemibold
    private const double MediumWeight = 0.23;  // NSFontWeightMedium
    private const ulong RoundLineCap = 1;      // NSLineCapStyleRound
    private const ulong PngFileType = 4;       // NSBitmapImageFileTypePNG

    private readonly double _height;
    private readonly IntPtr _pool;
    private readonly IntPtr _rep;
    private bool _drawing;

    static AppKitThumbnailCanvas() =>
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    public AppKitThumbnailCanvas(double widthPoints, double heightPoints, double scale)
    {
        _height = heightPoints;
        _pool = objc_autoreleasePoolPush();
        _rep = InitBitmapRep(
            Send(Class("NSBitmapImageRep"), Sel("alloc")),
            Sel("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bytesPerRow:bitsPerPixel:"),
            IntPtr.Zero,
            (long)Math.Round(widthPoints * scale),
            (long)Math.Round(heightPoints * scale),
            8, 4, true, false, NSString("NSDeviceRGBColorSpace"), 0, 0);
        SendSize(_rep, Sel("setSize:"), new CGSize { Width = widthPoints, Height = heightPoints });

        var context = SendPtr(Class("NSGraphicsContext"), Sel("graphicsContextWithBitmapImageRep:"), _rep);
        Send(Class("NSGraphicsContext"), Sel("saveGraphicsState"));
        SendPtr(Class("NSGraphicsContext"), Sel("setCurrentContext:"), context);
        _drawing = true;
    }

    public string FontDescription
    {
        get
        {
            var name = Send(Font(9, ThumbnailFont.Text), Sel("fontName"));
            var utf8 = name == IntPtr.Zero ? IntPtr.Zero : Send(name, Sel("UTF8String"));
            return (utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8)) ?? "?";
        }
    }

    public Size Measure(string text, double size, ThumbnailFont font)
    {
        var measured = SendSizeReturn(NSString(text), Sel("sizeWithAttributes:"), Attributes(size, font, Colors.Black));
        return new Size(measured.Width, measured.Height);
    }

    public void DrawText(string text, double left, double top, double size, ThumbnailFont font, Color colour)
    {
        var attributes = Attributes(size, font, colour);
        var height = SendSizeReturn(NSString(text), Sel("sizeWithAttributes:"), attributes).Height;
        SendPointPtr(NSString(text), Sel("drawAtPoint:withAttributes:"),
            new CGPoint { X = left, Y = _height - top - height }, attributes);
    }

    public void StrokeCircle(Point centre, double radius, double width, Color colour) =>
        Stroke(Oval(centre, radius), width, colour);

    public void FillCircle(Point centre, double radius, Color colour)
    {
        var path = Oval(centre, radius);
        Send(NSColor(colour), Sel("setFill"));
        Send(path, Sel("fill"));
    }

    public void StrokeArc(Point centre, double radius, double fraction, double width, Color colour)
    {
        if (fraction >= 0.999)
        {
            StrokeCircle(centre, radius, width, colour);
            return;
        }

        // Twelve o'clock is 90° with a bottom-left origin; clockwise sweeps towards smaller angles.
        var path = Send(Class("NSBezierPath"), Sel("bezierPath"));
        SendArc(path, Sel("appendBezierPathWithArcWithCenter:radius:startAngle:endAngle:clockwise:"),
            new CGPoint { X = centre.X, Y = _height - centre.Y }, radius, 90, 90 - 360 * fraction, true);
        SendULong(path, Sel("setLineCapStyle:"), RoundLineCap);
        Stroke(path, width, colour);
    }

    public byte[] EncodePng()
    {
        Finish();
        var data = SendULongPtr(_rep, Sel("representationUsingType:properties:"), PngFileType,
            Send(Class("NSDictionary"), Sel("dictionary")));
        var length = (int)SendULongReturn(data, Sel("length"));
        var png = new byte[length];
        Marshal.Copy(Send(data, Sel("bytes")), png, 0, length);
        return png;
    }

    public void Dispose()
    {
        Finish();
        Send(_rep, Sel("release"));
        objc_autoreleasePoolPop(_pool);
    }

    private void Finish()
    {
        if (!_drawing) return;
        Send(Class("NSGraphicsContext"), Sel("restoreGraphicsState"));
        _drawing = false;
    }

    private IntPtr Oval(Point centre, double radius) =>
        SendRect(Class("NSBezierPath"), Sel("bezierPathWithOvalInRect:"), new CGRect
        {
            Origin = new CGPoint { X = centre.X - radius, Y = _height - centre.Y - radius },
            Size = new CGSize { Width = 2 * radius, Height = 2 * radius }
        });

    private static void Stroke(IntPtr path, double width, Color colour)
    {
        SendDouble(path, Sel("setLineWidth:"), width);
        Send(NSColor(colour), Sel("setStroke"));
        Send(path, Sel("stroke"));
    }

    private static IntPtr Font(double size, ThumbnailFont font) => font == ThumbnailFont.Letter
        ? SendTwoDoubles(Class("NSFont"), Sel("systemFontOfSize:weight:"), size, SemiboldWeight)
        : SendTwoDoubles(Class("NSFont"), Sel("monospacedDigitSystemFontOfSize:weight:"), size, MediumWeight);

    private static IntPtr NSColor(Color colour) =>
        SendFourDoubles(Class("NSColor"), Sel("colorWithSRGBRed:green:blue:alpha:"),
            colour.R / 255.0, colour.G / 255.0, colour.B / 255.0, colour.A / 255.0);

    // The values of NSFontAttributeName and NSForegroundColorAttributeName.
    private static IntPtr Attributes(double size, ThumbnailFont font, Color colour) =>
        SendDictionary(Class("NSDictionary"), Sel("dictionaryWithObjects:forKeys:count:"),
            new[] { Font(size, font), NSColor(colour) },
            new[] { NSString("NSFont"), NSString("NSColor") },
            2);

    private static IntPtr NSString(string text) =>
        SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), text);

    private static IntPtr Class(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    [DllImport(ObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendDouble(IntPtr receiver, IntPtr selector, double value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendTwoDoubles(IntPtr receiver, IntPtr selector, double first, double second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendFourDoubles(IntPtr receiver, IntPtr selector, double first, double second, double third, double fourth);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendULong(IntPtr receiver, IntPtr selector, ulong value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern ulong SendULongReturn(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendULongPtr(IntPtr receiver, IntPtr selector, ulong value, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize size);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGSize SendSizeReturn(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendPointPtr(IntPtr receiver, IntPtr selector, CGPoint point, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendRect(IntPtr receiver, IntPtr selector, CGRect rect);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendDictionary(IntPtr receiver, IntPtr selector, IntPtr[] objects, IntPtr[] keys, ulong count);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendArc(IntPtr receiver, IntPtr selector, CGPoint centre, double radius,
        double startAngle, double endAngle, [MarshalAs(UnmanagedType.I1)] bool clockwise);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr InitBitmapRep(IntPtr receiver, IntPtr selector, IntPtr planes, long pixelsWide,
        long pixelsHigh, long bitsPerSample, long samplesPerPixel, [MarshalAs(UnmanagedType.I1)] bool hasAlpha,
        [MarshalAs(UnmanagedType.I1)] bool isPlanar, IntPtr colorSpaceName, long bytesPerRow, long bitsPerPixel);
}
