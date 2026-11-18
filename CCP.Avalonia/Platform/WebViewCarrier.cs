using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// Document-start scripts for WebKitGTK, WPE and WKWebView.
internal static class WebViewCarrier
{
    private delegate int Idle(IntPtr data);
    private delegate uint IdleAdd(Idle callback, IntPtr data);
    private delegate IntPtr Ref(IntPtr value);
    private delegate void Unref(IntPtr value);
    private delegate IntPtr GetManager(IntPtr view);
    private delegate IntPtr NewScript([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int frames, int time, IntPtr allow, IntPtr block);
    private delegate void AddScript(IntPtr manager, IntPtr script);
    private static readonly Idle Apply = OnIdle;

    private sealed record Request(IntPtr View, string Script, GetManager Manager, NewScript New, AddScript Add,
        Unref ScriptUnref, Unref ViewUnref, TaskCompletionSource<bool> Done, bool AllFrames);

    internal static Task<bool> InstallAsync(IPlatformHandle? handle, string script, bool allFrames = false)
    {
        if (OperatingSystem.IsMacOS() && allFrames && handle is IAppleWKWebViewPlatformHandle apple)
            return Task.FromResult(InstallApple(apple, script));
        var view = handle switch { IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView, ILinuxWpePlatformHandle wpe => wpe.WebKitWebView, _ => IntPtr.Zero };
        if (!OperatingSystem.IsLinux() || view == IntPtr.Zero || (handle is ILinuxWpePlatformHandle && !allFrames)) return Task.FromResult(false);
        try
        {
            // Only use the WebKit already loaded by Avalonia, to avoid loading a second libsoup.
            IntPtr webkit = IntPtr.Zero;
            foreach (var name in handle is ILinuxWpePlatformHandle ? new[] { "libWPEWebKit-2.0.so.1" } : ScrolllerWebKitFilter.Libraries)
                if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
            var glib = ScrolllerWebKitFilter.LoadedLibrary("libglib-2.0.so.0");
            var objects = ScrolllerWebKitFilter.LoadedLibrary("libgobject-2.0.so.0");
            if (webkit == IntPtr.Zero || glib == IntPtr.Zero || objects == IntPtr.Zero) return Task.FromResult(false);
            T Get<T>(IntPtr lib, string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            var release = Get<Unref>(objects, "g_object_unref");
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new Request(view, script,
                Get<GetManager>(webkit, "webkit_web_view_get_user_content_manager"),
                Get<NewScript>(webkit, "webkit_user_script_new"),
                Get<AddScript>(webkit, "webkit_user_content_manager_add_script"),
                Get<Unref>(webkit, "webkit_user_script_unref"), release, done, allFrames);
            var enqueue = Get<IdleAdd>(glib, "g_idle_add");
            Get<Ref>(objects, "g_object_ref")(request.View);
            var state = GCHandle.Alloc(request);
            try { if (enqueue(Apply, GCHandle.ToIntPtr(state)) != 0) return done.Task; }
            catch { state.Free(); release(request.View); throw; }
            state.Free();
            release(request.View);
        }
        catch (Exception ex) { Log.Debug("WebViewCarrier: install unavailable: {E}", ex.Message); }
        return Task.FromResult(false);
    }

    // WKUserScript is evaluated in each frame, including cross-origin frames. The retained
    // handle belongs to us; the content controller retains the user script after adding it.
    private static bool InstallApple(IAppleWKWebViewPlatformHandle handle, string source)
    {
        IntPtr view = IntPtr.Zero, script = IntPtr.Zero;
        try
        {
            view = handle.GetWKWebViewRetained();
            if (view == IntPtr.Zero) return false;
            var text = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), source);
            var allocated = Send(objc_getClass("WKUserScript"), sel_registerName("alloc"));
            script = SendScript(allocated, sel_registerName("initWithSource:injectionTime:forMainFrameOnly:"), text, 0, 0);
            var config = Send(view, sel_registerName("configuration"));
            var controller = Send(config, sel_registerName("userContentController"));
            if (script == IntPtr.Zero || controller == IntPtr.Zero) return false;
            SendOne(controller, sel_registerName("addUserScript:"), script);
            return true;
        }
        catch (Exception ex) { Log.Debug("WebViewCarrier: WKUserScript install failed: {E}", ex.Message); return false; }
        finally
        {
            if (script != IntPtr.Zero) Send(script, sel_registerName("release"));
            if (view != IntPtr.Zero) Send(view, sel_registerName("release"));
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr obj, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr SendScript(IntPtr obj, IntPtr selector, IntPtr text, long time, byte mainOnly);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendOne(IntPtr obj, IntPtr selector, IntPtr value);

    private static int OnIdle(IntPtr data)
    {
        var state = GCHandle.FromIntPtr(data);
        var request = (Request)state.Target!;
        var ok = false;
        try
        {
            var manager = request.Manager(request.View);
            // ALL_FRAMES or TOP_FRAME, at START before the page scripts.
            var script = manager == IntPtr.Zero ? IntPtr.Zero : request.New(request.Script, request.AllFrames ? 0 : 1, 0, IntPtr.Zero, IntPtr.Zero);
            if (script != IntPtr.Zero)
            {
                try { request.Add(manager, script); ok = true; }
                finally { request.ScriptUnref(script); }
            }
        }
        catch (Exception ex) { Log.Debug("WebViewCarrier: install failed: {E}", ex.Message); }
        finally { request.ViewUnref(request.View); state.Free(); request.Done.TrySetResult(ok); }
        return 0;
    }
}
