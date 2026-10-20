using System.Runtime.InteropServices;
using System.Text;

namespace TidalRpc;

// Menu bar item built on the Objective-C runtime, so the Mac app stays a single executable with no AppKit project.
// Everything here runs on the main thread, which is also where the event loop and its AppKit pump run.
internal sealed unsafe partial class MenuBar : IDisposable
{
    // Objective-C class lookups return nil until AppKit is loaded, and messages to nil silently do nothing,
    // so load the framework before anything else touches it.
    static MenuBar() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    private readonly Func<(string NowPlaying, string Status)> snapshot;
    private readonly IntPtr target;
    private readonly IntPtr statusItem;
    private readonly IntPtr nowPlayingItem;
    private readonly IntPtr statusLineItem;
    // Kept alive for the life of the process: the runtime holds only function pointers to these.
    private readonly Native.ActionCallback menuOpening;
    private readonly Native.ActionCallback reportCallback;
    private readonly Native.ActionCallback quitCallback;
    private readonly Native.ActionCallback removeCallback;
    private readonly Action reportProblem;
    private readonly Action quit;
    private readonly Action removeFromLogin;

    public MenuBar(Func<(string NowPlaying, string Status)> snapshot, Action reportProblem, Action quit, Action removeFromLogin)
    {
        this.snapshot = snapshot;
        this.reportProblem = reportProblem;
        this.quit = quit;
        this.removeFromLogin = removeFromLogin;

        var app = Native.Send(Cls("NSApplication"), Sel("sharedApplication"));
        // Accessory: a menu bar item only, with no Dock icon.
        Native.Send(app, Sel("setActivationPolicy:"), (IntPtr)1);
        Native.Send(app, Sel("finishLaunching"));

        menuOpening = (_, _, _) => Guard(Refresh);
        reportCallback = (_, _, _) => Guard(this.reportProblem);
        quitCallback = (_, _, _) => Guard(this.quit);
        removeCallback = (_, _, _) => Guard(this.removeFromLogin);
        target = CreateTarget();

        var menu = Native.Send(Native.Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
        Native.Send(menu, Sel("setDelegate:"), target);
        nowPlayingItem = MenuItem("Nothing playing in TIDAL", IntPtr.Zero, IntPtr.Zero);
        statusLineItem = MenuItem("Starting…", IntPtr.Zero, IntPtr.Zero);
        Native.Send(menu, Sel("addItem:"), nowPlayingItem);
        Native.Send(menu, Sel("addItem:"), statusLineItem);
        Native.Send(menu, Sel("addItem:"), Native.Send(Cls("NSMenuItem"), Sel("separatorItem")));
        Native.Send(menu, Sel("addItem:"), MenuItem("Report a problem…", Sel("reportProblem:"), target));
        Native.Send(menu, Sel("addItem:"), MenuItem("Quit for now", Sel("quitTidalRpc:"), target));
        Native.Send(menu, Sel("addItem:"), MenuItem("Stop and remove from login", Sel("removeFromLogin:"), target));

        statusItem = Native.SendDouble(Native.Send(Cls("NSStatusBar"), Sel("systemStatusBar")), Sel("statusItemWithLength:"), -1);
        Native.Send(statusItem, Sel("retain"));
        var button = Native.Send(statusItem, Sel("button"));
        var icon = LoadIcon();
        if (icon != IntPtr.Zero) Native.Send(button, Sel("setImage:"), icon);
        else Native.Send(button, Sel("setTitle:"), NSString("TIDAL RPC"));
        Native.Send(button, Sel("setToolTip:"), NSString("TIDAL RPC is running"));
        Native.Send(statusItem, Sel("setMenu:"), menu);
    }

    // Runs one short slice of AppKit's event queue, so menu clicks are handled while the event loop keeps running.
    // Returns after at most 50 ms, so the loop stays responsive and idle CPU stays near zero.
    public static void Pump()
    {
        var pool = Native.objc_autoreleasePoolPush();
        try
        {
            var app = Native.Send(Cls("NSApplication"), Sel("sharedApplication"));
            var until = Native.SendDouble(Cls("NSDate"), Sel("dateWithTimeIntervalSinceNow:"), 0.05);
            var mode = NSString("kCFRunLoopDefaultMode");
            var sendEvent = Native.SendEvent(app, Sel("nextEventMatchingMask:untilDate:inMode:dequeue:"), ulong.MaxValue, until, mode, 1);
            if (sendEvent != IntPtr.Zero) Native.Send(app, Sel("sendEvent:"), sendEvent);
        }
        catch (Exception ex) { MacHost.Log($"Menu bar error: {ex.GetType().Name}"); }
        finally { Native.objc_autoreleasePoolPop(pool); }
    }

    private void Refresh()
    {
        var (nowPlaying, status) = snapshot();
        Native.Send(nowPlayingItem, Sel("setTitle:"), NSString(nowPlaying));
        Native.Send(statusLineItem, Sel("setTitle:"), NSString(status));
    }

    public void Dispose()
    {
        Native.Send(Native.Send(Cls("NSStatusBar"), Sel("systemStatusBar")), Sel("removeStatusItem:"), statusItem);
    }

    // A callback from the runtime must not throw across native frames, so log and carry on.
    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MacHost.Log($"Menu action failed: {ex.GetType().Name}"); }
    }

    private IntPtr CreateTarget()
    {
        var cls = Native.objc_allocateClassPair(Cls("NSObject"), "TidalRpcMenuTarget", 0);
        Native.class_addMethod(cls, Sel("menuNeedsUpdate:"), Marshal.GetFunctionPointerForDelegate(menuOpening), "v@:@");
        Native.class_addMethod(cls, Sel("reportProblem:"), Marshal.GetFunctionPointerForDelegate(reportCallback), "v@:@");
        Native.class_addMethod(cls, Sel("quitTidalRpc:"), Marshal.GetFunctionPointerForDelegate(quitCallback), "v@:@");
        Native.class_addMethod(cls, Sel("removeFromLogin:"), Marshal.GetFunctionPointerForDelegate(removeCallback), "v@:@");
        Native.objc_registerClassPair(cls);
        return Native.Send(Native.Send(cls, Sel("alloc")), Sel("init"));
    }

    private static IntPtr MenuItem(string title, IntPtr action, IntPtr target)
    {
        var item = Native.Send(Native.Send(Cls("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), NSString(title), action, NSString(""));
        // With no action the item stays disabled, which is what the info lines need.
        if (target != IntPtr.Zero) Native.Send(item, Sel("setTarget:"), target);
        return item;
    }

    private static IntPtr LoadIcon()
    {
        using var stream = typeof(MenuBar).Assembly.GetManifestResourceStream("TidalRpc.MenuBarIcon.png");
        if (stream is null) return IntPtr.Zero;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        fixed (byte* pointer = bytes)
        {
            // NSData copies the bytes, so the pinned array is not needed after this call.
            var data = Native.SendBytes(Cls("NSData"), Sel("dataWithBytes:length:"), (IntPtr)pointer, (nuint)bytes.Length);
            var image = Native.Send(Native.Send(Cls("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            // Template images are tinted by the system for light and dark menu bars. 18 pt is the standard status item size.
            Native.Send(image, Sel("setTemplate:"), (IntPtr)1);
            Native.SendSize(image, Sel("setSize:"), new Native.Size { Width = 18, Height = 18 });
            return image;
        }
    }

    private static IntPtr Cls(string name) => Native.objc_getClass(name);
    private static IntPtr Sel(string name) => Native.sel_registerName(name);

    private static IntPtr NSString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* pointer = bytes) return Native.Send(Cls("NSString"), Sel("stringWithUTF8String:"), (IntPtr)pointer);
    }

    private static partial class Native
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ActionCallback(IntPtr self, IntPtr selector, IntPtr sender);

        [StructLayout(LayoutKind.Sequential)]
        public struct Size { public double Width; public double Height; }

        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr objc_getClass(string name);
        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr sel_registerName(string name);
        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
        [LibraryImport(ObjC)] public static partial void objc_registerClassPair(IntPtr cls);
        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial byte class_addMethod(IntPtr cls, IntPtr name, IntPtr implementation, string types);
        [LibraryImport(ObjC)] public static partial IntPtr objc_autoreleasePoolPush();
        [LibraryImport(ObjC)] public static partial void objc_autoreleasePoolPop(IntPtr pool);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr Send(IntPtr self, IntPtr selector);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr Send(IntPtr self, IntPtr selector, IntPtr a);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr Send(IntPtr self, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr SendDouble(IntPtr self, IntPtr selector, double value);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr SendSize(IntPtr self, IntPtr selector, Size value);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr SendBytes(IntPtr self, IntPtr selector, IntPtr bytes, nuint length);
        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] public static partial IntPtr SendEvent(IntPtr self, IntPtr selector, ulong mask, IntPtr date, IntPtr mode, nint dequeue);
    }
}
