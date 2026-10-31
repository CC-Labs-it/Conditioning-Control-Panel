using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF BrowserService.cs:119-122, 245-246. Engine mute includes cross-origin frames.
internal static class WebViewAudio
{
    private delegate int Idle(IntPtr data);
    private delegate uint IdleAdd(Idle callback, IntPtr data);
    private delegate IntPtr Ref(IntPtr view);
    private delegate void Unref(IntPtr view);
    private delegate void Mute(IntPtr view, int muted);
    private static readonly Idle Apply = OnIdle;

    internal static Task<bool> SetMutedAsync(IPlatformHandle? handle, bool muted)
    {
        try
        {
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle w && w.CoreWebView2 != IntPtr.Zero)
                return Task.FromResult(SetWindows(w.CoreWebView2, muted));
            if (OperatingSystem.IsLinux() && handle is IGtkWebViewPlatformHandle g && g.WebKitWebView != IntPtr.Zero)
                return SetGtk(g.WebKitWebView, muted);
        }
        catch (Exception ex) { Log.Debug("WebViewAudio: mute unavailable: {E}", ex.Message); }
        return Task.FromResult(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SetWindows(IntPtr core, bool muted)
    {
        Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(core).IsMuted = muted;
        return true;
    }

    private sealed record Request(IntPtr View, bool Muted, Mute Set, Unref Release, TaskCompletionSource<bool> Done);

    private static Task<bool> SetGtk(IntPtr view, bool muted)
    {
        // Use only the WebKit already loaded by Avalonia. Loading another libsoup can abort the process.
        IntPtr webkit = IntPtr.Zero;
        foreach (var name in ScrolllerWebKitFilter.Libraries)
            if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
        var glib = ScrolllerWebKitFilter.LoadedLibrary("libglib-2.0.so.0");
        var objects = ScrolllerWebKitFilter.LoadedLibrary("libgobject-2.0.so.0");
        if (webkit == IntPtr.Zero || glib == IntPtr.Zero || objects == IntPtr.Zero) return Task.FromResult(false);
        T Get<T>(IntPtr lib, string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        var set = Get<Mute>(webkit, "webkit_web_view_set_is_muted");
        var release = Get<Unref>(objects, "g_object_unref");
        var enqueue = Get<IdleAdd>(glib, "g_idle_add");
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Get<Ref>(objects, "g_object_ref")(view); // survive adapter detachment until the GLib callback
        var state = GCHandle.Alloc(new Request(view, muted, set, release, done));
        try
        {
            if (enqueue(Apply, GCHandle.ToIntPtr(state)) != 0) return done.Task;
        }
        catch { state.Free(); release(view); throw; }
        state.Free();
        release(view);
        return Task.FromResult(false);
    }

    private static int OnIdle(IntPtr data)
    {
        var state = GCHandle.FromIntPtr(data);
        var r = (Request)state.Target!;
        var ok = false;
        try { r.Set(r.View, r.Muted ? 1 : 0); ok = true; }
        catch (Exception ex) { Log.Debug("WebViewAudio: mute failed: {E}", ex.Message); }
        finally { r.Release(r.View); state.Free(); r.Done.TrySetResult(ok); }
        return 0;
    }
}
