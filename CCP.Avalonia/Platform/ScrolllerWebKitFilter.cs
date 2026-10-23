using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
/// <para>The rule compiles once per process into a filter store under the profile, on the GLib thread (every call into
/// WebKit is queued there with g_idle_add). The library is the one Avalonia.Controls.WebView already loaded (found with
/// RTLD_NOLOAD: loading the other of libwebkit2gtk-4.0 / 4.1 would pull a second libsoup and abort the process). WPE is
/// not covered (only on CCP_LINUX_WEBVIEW=wpe). Never throws.</para>
/// </summary>
internal static class ScrolllerWebKitFilter
{
    internal const string Identifier = "ccp-scrolller-block";

    /// <summary>One WebKit content-blocker rule: any scheme, scrolller.com or any subdomain, any resource type.</summary>
    internal const string RulesJson =
        """[{"trigger":{"url-filter":"^[^:]+://+([^:/]+\\.)?scrolller\\.com[/:]"},"action":{"type":"block"}}]""";

    /// <summary>The libraries Avalonia.Controls.WebView can have loaded, newest first.</summary>
    internal static readonly string[] Libraries = { "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37" };

    private delegate IntPtr Fn1(IntPtr a);
    private delegate IntPtr StoreNew([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    private delegate void StoreSave(IntPtr store, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, IntPtr bytes, IntPtr cancellable,
        AsyncReady callback, IntPtr userData);
    private delegate IntPtr StoreSaveFinish(IntPtr store, IntPtr result, out IntPtr error);
    private delegate void AddFilter(IntPtr manager, IntPtr filter);
    private delegate void AsyncReady(IntPtr source, IntPtr result, IntPtr userData);
    private delegate int SourceFunc(IntPtr data);

    private static readonly object Gate = new();
    private static readonly List<IntPtr> Pending = new();   // content managers (ref'd) waiting for the compiled filter
    private static IntPtr _filter;   // the compiled filter, kept for the process
    private static bool _compiling, _failed;
    private static Api? _api;
    private static readonly AsyncReady OnSaved = Saved;      // kept alive: GLib calls them
    private static readonly SourceFunc OnIdle = Idle;
    private static readonly Queue<IntPtr> Views = new();

    /// <summary>Queues the filter onto one WebKitWebView (the adapter's platform handle). Safe from any thread.</summary>
    public static void Attach(IntPtr webKitWebView)
    {
        if (webKitWebView == IntPtr.Zero || !OperatingSystem.IsLinux()) return;
        try
        {
            var api = _api ??= Api.Load();
            if (api == null) { Log.Warning("ScrolllerWebKitFilter: no WebKitGTK content filter API; Scrolller is not blocked in web views"); return; }
            api.ObjectRef(webKitWebView);   // alive until the GLib thread has seen it
            lock (Gate) Views.Enqueue(webKitWebView);
            api.IdleAdd(OnIdle, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: attach failed: {E}", ex.Message); }
    }

    /// <summary>On the GLib thread: each queued view's content manager gets the filter, compiled first if need be.</summary>
    private static int Idle(IntPtr _)
    {
        try
        {
            var api = _api!;
            while (true)
            {
                IntPtr view;
                lock (Gate) { if (Views.Count == 0) break; view = Views.Dequeue(); }
                var manager = api.GetManager(view);
                if (manager != IntPtr.Zero)
                {
                    if (_filter != IntPtr.Zero) api.AddFilter(manager, _filter);
                    else if (!_failed) { api.ObjectRef(manager); Pending.Add(manager); }
                }
                api.ObjectUnref(view);
            }
            if (_filter == IntPtr.Zero && !_failed && !_compiling && Pending.Count > 0) Compile(api);
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: idle failed: {E}", ex.Message); }
        return 0;   // G_SOURCE_REMOVE
    }

    private static void Compile(Api api)
    {
        _compiling = true;
        var dir = Path.Combine(CorePaths.UserData, "webkit-filters");
        Directory.CreateDirectory(dir);
        var store = api.StoreNew(dir);
        var json = Encoding.UTF8.GetBytes(RulesJson);
        var bytes = api.BytesNew(json, (UIntPtr)json.Length);
        api.StoreSave(store, Identifier, bytes, IntPtr.Zero, OnSaved, IntPtr.Zero);
        api.BytesUnref(bytes);
    }

    private static void Saved(IntPtr store, IntPtr result, IntPtr _)
    {
        try
        {
            var api = _api!;
            _filter = api.StoreSaveFinish(store, result, out var error);
            _compiling = false;
            if (_filter == IntPtr.Zero)
            {
                _failed = true;
                Log.Warning("ScrolllerWebKitFilter: the block rule did not compile ({E}); Scrolller is not blocked in web views",
                    error == IntPtr.Zero ? "no error" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "error");
                if (error != IntPtr.Zero) api.ErrorFree(error);
            }
            foreach (var manager in Pending)
            {
                if (_filter != IntPtr.Zero) api.AddFilter(manager, _filter);
                api.ObjectUnref(manager);
            }
            Pending.Clear();
            api.ObjectUnref(store);
            if (_filter != IntPtr.Zero) Log.Information("ScrolllerWebKitFilter: Scrolller blocked in WebKitGTK web views");
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebKitFilter: compile failed: {E}", ex.Message); }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr DlopenDl(string file, int mode);

    [DllImport("libc.so.6", EntryPoint = "dlopen")]
    private static extern IntPtr DlopenC(string file, int mode);

    /// <summary>The handle of a library ALREADY in the process, or zero; never loads one (RTLD_LAZY | RTLD_NOLOAD).</summary>
    private static IntPtr Loaded(string name)
    {
        const int flags = 0x1 | 0x4;
        try { return DlopenDl(name, flags); }
        catch (Exception) { try { return DlopenC(name, flags); } catch (Exception) { return IntPtr.Zero; } }
    }

    private sealed class Api
    {
        public required Fn1 GetManager, ObjectRef;
        public required Action<IntPtr> ObjectUnref, BytesUnref, ErrorFree;
        public required StoreNew StoreNew;
        public required StoreSave StoreSave;
        public required StoreSaveFinish StoreSaveFinish;
        public required AddFilter AddFilter;
        public required Func<byte[], UIntPtr, IntPtr> BytesNew;
        public required Func<SourceFunc, IntPtr, uint> IdleAdd;

        private delegate IntPtr BytesNewFn(byte[] data, UIntPtr size);
        private delegate uint IdleAddFn(SourceFunc f, IntPtr data);
        private delegate void Fn1Void(IntPtr a);

        public static Api? Load()
        {
            IntPtr webkit = IntPtr.Zero;
            foreach (var name in Libraries) if ((webkit = Loaded(name)) != IntPtr.Zero) break;
            var glib = Loaded("libglib-2.0.so.0");
            var gobject = Loaded("libgobject-2.0.so.0");
            if (webkit == IntPtr.Zero || glib == IntPtr.Zero || gobject == IntPtr.Zero) return null;
            T F<T>(IntPtr lib, string sym) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, sym));
            if (!NativeLibrary.TryGetExport(webkit, "webkit_user_content_manager_add_filter", out _)) return null;   // < 2.24
            var unref = F<Fn1Void>(gobject, "g_object_unref");
            var bytesUnref = F<Fn1Void>(glib, "g_bytes_unref");
            var errorFree = F<Fn1Void>(glib, "g_error_free");
            var bytesNew = F<BytesNewFn>(glib, "g_bytes_new");
            var idleAdd = F<IdleAddFn>(glib, "g_idle_add");
            return new Api
            {
                GetManager = F<Fn1>(webkit, "webkit_web_view_get_user_content_manager"),
                ObjectRef = F<Fn1>(gobject, "g_object_ref"),
                ObjectUnref = p => unref(p),
                BytesUnref = p => bytesUnref(p),
                ErrorFree = p => errorFree(p),
                StoreNew = F<StoreNew>(webkit, "webkit_user_content_filter_store_new"),
                StoreSave = F<StoreSave>(webkit, "webkit_user_content_filter_store_save"),
                StoreSaveFinish = F<StoreSaveFinish>(webkit, "webkit_user_content_filter_store_save_finish"),
                AddFilter = F<AddFilter>(webkit, "webkit_user_content_manager_add_filter"),
                BytesNew = (d, n) => bytesNew(d, n),
                IdleAdd = (f, d) => idleAdd(f, d),
            };
        }
    }
}
