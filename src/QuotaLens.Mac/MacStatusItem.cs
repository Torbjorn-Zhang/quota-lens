using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuotaLens.Mac;

/// <summary>
/// A menu bar item created directly through the Objective-C runtime. Avalonia's TrayIcon always
/// asks AppKit for a square item (<c>NSSquareStatusItemLength</c>), which centre-crops a wide
/// icon, and it raises no click event on macOS. This item uses
/// <c>NSVariableStatusItemLength</c>, so the two ring groups fit, and a click calls back into .NET
/// so the panel can open straight from the icon.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacStatusItem : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.dylib";
    private const double VariableLength = -1;
    private const ulong LeftMouseUpMask = 1UL << 2;
    private const ulong RightMouseUpMask = 1UL << 4;
    private const string TargetClassName = "QuotaLensStatusItemTarget";

    // The delegate must stay reachable for as long as AppKit may call it.
    private static readonly ClickCallback ClickImplementation = OnClick;
    private static Action? _clickHandler;

    private readonly IntPtr _item;
    private readonly IntPtr _button;
    private readonly IntPtr _target;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClickCallback(IntPtr self, IntPtr selector, IntPtr sender);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    public MacStatusItem(Action onClick)
    {
        _clickHandler = onClick;

        var statusBar = Send(Class("NSStatusBar"), Sel("systemStatusBar"));
        _item = SendDouble(statusBar, Sel("statusItemWithLength:"), VariableLength);
        Send(_item, Sel("retain"));
        _button = Send(_item, Sel("button"));

        _target = Send(Send(TargetClass(), Sel("alloc")), Sel("init"));
        SendPtr(_button, Sel("setTarget:"), _target);
        SendPtr(_button, Sel("setAction:"), Sel("statusItemClicked:"));
        SendULong(_button, Sel("sendActionOn:"), LeftMouseUpMask | RightMouseUpMask);
    }

    /// <summary>
    /// Sets a PNG as the item image, displayed at <paramref name="widthPoints"/> ×
    /// <paramref name="heightPoints"/> (the PNG itself is rendered at 2× for Retina).
    /// </summary>
    public void SetImage(byte[] png, double widthPoints, double heightPoints)
    {
        var handle = GCHandle.Alloc(png, GCHandleType.Pinned);
        try
        {
            var data = SendBytes(Class("NSData"), Sel("dataWithBytes:length:"), handle.AddrOfPinnedObject(), (ulong)png.Length);
            var image = SendPtr(Send(Class("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            if (image == IntPtr.Zero) return;

            SendSize(image, Sel("setSize:"), new CGSize { Width = widthPoints, Height = heightPoints });
            SendBool(image, Sel("setTemplate:"), false);
            SendPtr(_button, Sel("setImage:"), image);
            Send(image, Sel("release"));
        }
        finally
        {
            handle.Free();
        }
    }

    public void SetToolTip(string text) =>
        SendPtr(_button, Sel("setToolTip:"), NSString(text));

    /// <summary>The item's frame on screen in points (Cocoa coordinates, origin at the bottom left).</summary>
    public CGRect ScreenFrame
    {
        get
        {
            var window = Send(_button, Sel("window"));
            return window == IntPtr.Zero ? default : SendRectReturn(window, Sel("frame"));
        }
    }

    /// <summary>Bitmask of mouse buttons currently held down anywhere on screen (no permission needed).</summary>
    public static ulong PressedMouseButtons() => SendULongReturn(Class("NSEvent"), Sel("pressedMouseButtons"));

    /// <summary>Cursor position in points, Cocoa coordinates (origin at the bottom left of the primary screen).</summary>
    public static CGPoint MouseLocation() => SendPointReturn(Class("NSEvent"), Sel("mouseLocation"));

    /// <summary>Frame of the primary screen (the one with the menu bar) in points.</summary>
    public static CGRect PrimaryScreenFrame()
    {
        var screens = Send(Class("NSScreen"), Sel("screens"));
        var primary = SendIndex(screens, Sel("objectAtIndex:"), 0);
        return primary == IntPtr.Zero ? default : SendRectReturn(primary, Sel("frame"));
    }

    /// <summary>Brings this accessory app forward so the panel receives focus and loses it on outside clicks.</summary>
    public static void ActivateApp()
    {
        var app = Send(Class("NSApplication"), Sel("sharedApplication"));
        if (SendRespondsTo(app, Sel("respondsToSelector:"), Sel("activate")))
        {
            Send(app, Sel("activate"));
        }
        else
        {
            SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        }
    }

    /// <summary>Simulates a click, used by the --self-test flag to exercise the click path.</summary>
    public void PerformClick() => SendPtr(_button, Sel("performClick:"), IntPtr.Zero);

    public void Dispose()
    {
        var statusBar = Send(Class("NSStatusBar"), Sel("systemStatusBar"));
        SendPtr(statusBar, Sel("removeStatusItem:"), _item);
        Send(_item, Sel("release"));
        Send(_target, Sel("release"));
        _clickHandler = null;
    }

    private static void OnClick(IntPtr self, IntPtr selector, IntPtr sender) => _clickHandler?.Invoke();

    private static IntPtr TargetClass()
    {
        var existing = objc_getClass(TargetClassName);
        if (existing != IntPtr.Zero) return existing;

        var cls = objc_allocateClassPair(Class("NSObject"), TargetClassName, IntPtr.Zero);
        class_addMethod(
            cls,
            Sel("statusItemClicked:"),
            Marshal.GetFunctionPointerForDelegate(ClickImplementation),
            "v@:@");
        objc_registerClassPair(cls);
        return cls;
    }

    private static IntPtr NSString(string text) =>
        SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), text);

    private static IntPtr Class(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    [DllImport(ObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr extraBytes);
    [DllImport(ObjC)] private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendULong(IntPtr receiver, IntPtr selector, ulong argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, ulong length);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize size);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern ulong SendULongReturn(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGPoint SendPointReturn(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendRespondsTo(IntPtr receiver, IntPtr selector, IntPtr argument);

    // A 32-byte struct comes back in registers on arm64 but through a hidden pointer on x86-64.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGRect SendRectArm64(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern CGRect SendRectX64(IntPtr receiver, IntPtr selector);

    private static CGRect SendRectReturn(IntPtr receiver, IntPtr selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? SendRectArm64(receiver, selector)
            : SendRectX64(receiver, selector);
}
