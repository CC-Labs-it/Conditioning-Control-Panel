using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WebKitGTK's document-start counterpart to WebView2's built-in chrome.webview bridge.
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
        Unref ScriptUnref, Unref ViewUnref, TaskCompletionSource<bool> Done);

    internal static Task<bool> InstallAsync(IPlatformHandle? handle, string script)
    {
        if (!OperatingSystem.IsLinux() || handle is not IGtkWebViewPlatformHandle gtk || gtk.WebKitWebView == IntPtr.Zero)
            return Task.FromResult(false);
        try
        {
            // Only use the WebKit already loaded by Avalonia, to avoid loading a second libsoup.
            IntPtr webkit = IntPtr.Zero;
            foreach (var name in ScrolllerWebKitFilter.Libraries)
                if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
            var glib = ScrolllerWebKitFilter.LoadedLibrary("libglib-2.0.so.0");
            var objects = ScrolllerWebKitFilter.LoadedLibrary("libgobject-2.0.so.0");
            if (webkit == IntPtr.Zero || glib == IntPtr.Zero || objects == IntPtr.Zero) return Task.FromResult(false);
            T Get<T>(IntPtr lib, string name) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            var release = Get<Unref>(objects, "g_object_unref");
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new Request(gtk.WebKitWebView, script,
                Get<GetManager>(webkit, "webkit_web_view_get_user_content_manager"),
                Get<NewScript>(webkit, "webkit_user_script_new"),
                Get<AddScript>(webkit, "webkit_user_content_manager_add_script"),
                Get<Unref>(webkit, "webkit_user_script_unref"), release, done);
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

    private static int OnIdle(IntPtr data)
    {
        var state = GCHandle.FromIntPtr(data);
        var request = (Request)state.Target!;
        var ok = false;
        try
        {
            var manager = request.Manager(request.View);
            // TOP_FRAME and START: the page sees the carrier before its own scripts.
            var script = manager == IntPtr.Zero ? IntPtr.Zero : request.New(request.Script, 1, 0, IntPtr.Zero, IntPtr.Zero);
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
