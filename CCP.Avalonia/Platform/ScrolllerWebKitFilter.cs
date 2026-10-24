using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Scrolller licence on this head's Linux web views (WebKitGTK). WebKitGTK cannot answer or rewrite a request from
/// here (no response seam, no per-request user-agent), so a page that loads Scrolller itself (the in-app browser, Just Drop,
/// the Deeper preview, the leash punishment page) would go out with the engine's user-agent, uncharged and uncached. So
/// every such view gets a WebKit content filter (webkit_user_content_manager_add_filter, WebKitGTK 2.24+) that BLOCKS every
/// request to scrolller.com and its subdomains: the owner's "block" choice for the gap. This head's own pages never need
/// one, they get Scrolller only through the loopback relay (Core <see cref="ConditioningControlPanel.Services.Fyp.Online.ScrolllerRelay"/>).
/// <para>Live for the FIRST load: WebHost holds its first navigation until <see cref="Attach"/>'s callback says the filter
/// is on this view (false = it could not be had, and WebHost then refuses every page that is not this head's own). The
/// compiled filter is kept in a store under the profile (its identifier carries a hash of the rule), so a launch loads it
/// and compiles only when it is missing. Every WebKit call is queued on the GLib thread (g_idle_add), against the
/// library Avalonia.Controls.WebView already loaded (RTLD_NOLOAD: loading the other of libwebkit2gtk-4.0 / 4.1 would pull
/// a second libsoup and abort the process). WPE is not covered (only on CCP_LINUX_WEBVIEW=wpe). Never throws.</para>
/// </summary>
internal static class ScrolllerWebKitFilter
{
    /// <summary>One WebKit content-blocker rule: any scheme, scrolller.com or any subdomain, any resource type.</summary>
    internal const string RulesJson =
        """[{"trigger":{"url-filter":"^[^:]+://+([^:/]+\\.)?scrolller\\.com[/:]"},"action":{"type":"block"}}]""";

    /// <summary>The stored filter's name: a changed rule is a new name, so a stale compiled rule is never loaded.</summary>
    internal static readonly string Identifier =
        "ccp-scrolller-block-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RulesJson)))[..12].ToLowerInvariant();

    /// <summary>The libraries Avalonia.Controls.WebView can have loaded, newest first.</summary>
    internal static readonly string[] Libraries = { "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37" };

    private delegate IntPtr Fn1(IntPtr a);
    private delegate void Fn1Void(IntPtr a);
    private delegate IntPtr StoreNew([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    private delegate void StoreSave(IntPtr store, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, IntPtr bytes, IntPtr cancellable,
        AsyncReady callback, IntPtr userData);
    private delegate void StoreLoad(IntPtr store, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, IntPtr cancellable,
        AsyncReady callback, IntPtr userData);
    private delegate IntPtr StoreFinish(IntPtr store, IntPtr result, out IntPtr error);
    private delegate void AddFilter(IntPtr manager, IntPtr filter);
    private delegate void AsyncReady(IntPtr source, IntPtr result, IntPtr userData);
    private delegate int SourceFunc(IntPtr data);
    private delegate IntPtr BytesNewFn(byte[] data, UIntPtr size);
    private delegate uint IdleAddFn(SourceFunc f, IntPtr data);

    private static readonly object Gate = new();
    private static readonly Queue<(IntPtr View, Action<bool> Ready)> Views = new();
    // GLib thread only from here down.
    private static readonly List<(IntPtr Manager, Action<bool> Ready)> Pending = new();
    private static IntPtr _filter;
    private static bool _started, _failed;
    private static Api? _api;
    private static readonly AsyncReady OnLoaded = Loaded, OnSaved = Saved;   // kept alive: GLib calls them
    private static readonly SourceFunc OnIdle = Idle;

    /// <summary>Queues the filter onto one WebKitWebView (the adapter's platform handle). <paramref name="ready"/> runs once,
    /// on the GLib thread or the caller's: true when the filter is on that view, false when it cannot be.</summary>
    public static void Attach(IntPtr webKitWebView, Action<bool> ready)
    {
        if (webKitWebView == IntPtr.Zero || !OperatingSystem.IsLinux()) { ready(false); return; }
        try
        {
            var api = _api ??= Api.Load();
            if (api == null)
            {
                Log.Warning("ScrolllerWebKitFilter: no WebKitGTK content filter API (2.24+); only this head's own pages load");
                ready(false);
                return;
            }
            api.ObjectRef(webKitWebView);   // alive until the GLib thread has seen it
            lock (Gate) Views.Enqueue((webKitWebView, ready));
            api.IdleAdd(OnIdle, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warning("ScrolllerWebKitFilter: attach failed: {E}", ex.Message);
            ready(false);
        }
    }

    /// <summary>On the GLib thread: each queued view gets the filter, which is loaded (or compiled) first if need be.</summary>
    private static int Idle(IntPtr _)
    {
        var api = _api!;
        while (true)
        {
            (IntPtr View, Action<bool> Ready) next;
            lock (Gate) { if (Views.Count == 0) break; next = Views.Dequeue(); }
            try
            {
                var manager = api.GetManager(next.View);
                if (manager == IntPtr.Zero || _failed) Report(next.Ready, false);
                else if (_filter != IntPtr.Zero) { api.AddFilter(manager, _filter); Report(next.Ready, true); }
                else { api.ObjectRef(manager); Pending.Add((manager, next.Ready)); }
            }
            catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: idle failed: {E}", ex.Message); Report(next.Ready, false); }
            finally { try { api.ObjectUnref(next.View); } catch { } }
        }
        if (!_started && Pending.Count > 0)
        {
            _started = true;
            try
            {
                var dir = Path.Combine(CorePaths.UserData, "webkit-filters");
                Directory.CreateDirectory(dir);
                api.StoreLoad(api.StoreNew(dir), Identifier, IntPtr.Zero, OnLoaded, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: store failed: {E}", ex.Message); Finish(IntPtr.Zero, IntPtr.Zero); }
        }
        return 0;   // G_SOURCE_REMOVE
    }

    /// <summary>A stored filter from an earlier launch, or none (then the rule is compiled and stored).</summary>
    private static void Loaded(IntPtr store, IntPtr result, IntPtr _)
    {
        try
        {
            var api = _api!;
            var filter = api.LoadFinish(store, result, out var error);
            if (error != IntPtr.Zero) api.ErrorFree(error);
            if (filter != IntPtr.Zero) { Finish(filter, store); return; }
            var json = Encoding.UTF8.GetBytes(RulesJson);
            var bytes = api.BytesNew(json, (UIntPtr)json.Length);
            api.StoreSave(store, Identifier, bytes, IntPtr.Zero, OnSaved, IntPtr.Zero);
            api.BytesUnref(bytes);
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: load failed: {E}", ex.Message); Finish(IntPtr.Zero, store); }
    }

    private static void Saved(IntPtr store, IntPtr result, IntPtr _)
    {
        IntPtr filter = IntPtr.Zero;
        try
        {
            var api = _api!;
            filter = api.SaveFinish(store, result, out var error);
            if (filter == IntPtr.Zero)
                Log.Warning("ScrolllerWebKitFilter: the block rule did not compile ({E})",
                    error == IntPtr.Zero ? "no error" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "error");
            if (error != IntPtr.Zero) api.ErrorFree(error);
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: compile failed: {E}", ex.Message); }
        Finish(filter, store);
    }

    /// <summary>Every waiting view gets the filter (or a refusal); the filter is kept for the process.</summary>
    private static void Finish(IntPtr filter, IntPtr store)
    {
        var api = _api!;
        _filter = filter;
        _failed = filter == IntPtr.Zero;
        foreach (var (manager, ready) in Pending)
        {
            bool ok = false;
            try { if (!_failed) { api.AddFilter(manager, filter); ok = true; } }
            catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: add failed: {E}", ex.Message); }
            try { api.ObjectUnref(manager); } catch { }
            Report(ready, ok);
        }
        Pending.Clear();
        if (store != IntPtr.Zero) { try { api.ObjectUnref(store); } catch { } }
        if (_failed) Log.Warning("ScrolllerWebKitFilter: no block filter; only this head's own pages load in web views");
        else Log.Information("ScrolllerWebKitFilter: Scrolller blocked in WebKitGTK web views");
    }

    private static void Report(Action<bool> ready, bool ok)
    {
        try { ready(ok); } catch (Exception ex) { Log.Debug("ScrolllerWebKitFilter: ready callback failed: {E}", ex.Message); }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr DlopenDl(string file, int mode);

    [DllImport("libc.so.6", EntryPoint = "dlopen")]
    private static extern IntPtr DlopenC(string file, int mode);

    /// <summary>The handle of a library ALREADY in the process, or zero; never loads one (RTLD_LAZY | RTLD_NOLOAD).</summary>
    private static IntPtr LoadedLibrary(string name)
    {
        const int flags = 0x1 | 0x4;
        try { return DlopenDl(name, flags); }
        catch (Exception) { try { return DlopenC(name, flags); } catch (Exception) { return IntPtr.Zero; } }
    }

    private sealed class Api
    {
        public required Fn1 GetManager, ObjectRef;
        public required Fn1Void ObjectUnref, BytesUnref, ErrorFree;
        public required StoreNew StoreNew;
        public required StoreSave StoreSave;
        public required StoreLoad StoreLoad;
        public required StoreFinish SaveFinish, LoadFinish;
        public required AddFilter AddFilter;
        public required BytesNewFn BytesNew;
        public required IdleAddFn IdleAdd;

        public static Api? Load()
        {
            IntPtr webkit = IntPtr.Zero;
            foreach (var name in Libraries) if ((webkit = LoadedLibrary(name)) != IntPtr.Zero) break;
            var glib = LoadedLibrary("libglib-2.0.so.0");
            var gobject = LoadedLibrary("libgobject-2.0.so.0");
            if (webkit == IntPtr.Zero || glib == IntPtr.Zero || gobject == IntPtr.Zero) return null;
            if (!NativeLibrary.TryGetExport(webkit, "webkit_user_content_manager_add_filter", out _)) return null;   // < 2.24
            T F<T>(IntPtr lib, string sym) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, sym));
            return new Api
            {
                GetManager = F<Fn1>(webkit, "webkit_web_view_get_user_content_manager"),
                ObjectRef = F<Fn1>(gobject, "g_object_ref"),
                ObjectUnref = F<Fn1Void>(gobject, "g_object_unref"),
                BytesUnref = F<Fn1Void>(glib, "g_bytes_unref"),
                ErrorFree = F<Fn1Void>(glib, "g_error_free"),
                StoreNew = F<StoreNew>(webkit, "webkit_user_content_filter_store_new"),
                StoreSave = F<StoreSave>(webkit, "webkit_user_content_filter_store_save"),
                StoreLoad = F<StoreLoad>(webkit, "webkit_user_content_filter_store_load"),
                SaveFinish = F<StoreFinish>(webkit, "webkit_user_content_filter_store_save_finish"),
                LoadFinish = F<StoreFinish>(webkit, "webkit_user_content_filter_store_load_finish"),
                AddFilter = F<AddFilter>(webkit, "webkit_user_content_manager_add_filter"),
                BytesNew = F<BytesNewFn>(glib, "g_bytes_new"),
                IdleAdd = F<IdleAddFn>(glib, "g_idle_add"),
            };
        }
    }
}
