using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using ConditioningControlPanel.Services.JustDrop;
using Microsoft.Win32.SafeHandles;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF JustDropAssetHost.Attach/OnRequest/ServeAsync (:158-212): serves the Just Drop window's asset urls from Core
/// <see cref="JustDropAssetMap.Answer"/>. Avalonia.Controls.WebView has no response seam, so the engine comes from the
/// adapter's platform handle (WebHost.ConfigureNative, once per engine). WebView2 answers <c>https://justdrop-assets.ccp/*</c>
/// with a request filter. WebKitGTK cannot answer a foreign https host, so there the same ids ride a custom scheme,
/// <c>ccp-justdrop://justdrop-assets.ccp/*</c>, registered secure (an https page may load and fetch it) and CORS-enabled.
/// </summary>
internal static class JustDropAssetTransport
{
    internal static void Attach(IPlatformHandle? handle, JustDropAssetMap map)
    {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2 != IntPtr.Zero)
        {
            try { Windows(win.CoreWebView2, map); }
            catch (Exception ex) { Log.Warning("JustDropAssets: filter attach failed: {E}", ex.Message); }
        }
        else if (OperatingSystem.IsLinux() && handle is IGtkWebViewPlatformHandle gtk && gtk.WebKitWebView != IntPtr.Zero)
        {
            Track(map);
            try { WebKit.Register(gtk.WebKitWebView); }
            catch (Exception ex) { Log.Warning("JustDropAssets: scheme attach failed: {E}", ex.Message); }
        }
    }

    /// <summary>Window closed: this map's urls stop resolving on every engine.</summary>
    internal static void Detach(JustDropAssetMap map) { lock (Maps) Maps.Remove(map); }

    /// <summary>The urls one assets-request answers: WebView2 serves the map's own, WebKitGTK its scheme twin once the
    /// scheme is registered, any other engine none (an url nothing answers is worse than an empty list).</summary>
    internal static List<string> Urls(JustDropAssetMap map, bool video, int max)
    {
        if (OperatingSystem.IsWindows()) return map.List(video, max);
        bool served;
        lock (Maps) served = WebKit.Registered && Maps.Contains(map);
        return served ? map.List(video, max).ConvertAll(ToScheme) : new();
    }

    // ============================== WebKitGTK ==============================

    internal const string Scheme = "ccp-justdrop";

    private static readonly HashSet<JustDropAssetMap> Maps = new();

    internal static void Track(JustDropAssetMap map) { lock (Maps) Maps.Add(map); }

    internal static string ToScheme(string url) => Scheme + url[Uri.UriSchemeHttps.Length..];

    /// <summary>The answer for one scheme request, from the window whose map handed the id out. Never null.</summary>
    internal static JustDropAssetMap.Response Answer(string? method, string? uri, string? range)
    {
        JustDropAssetMap.Response? r = null;
        if (uri != null && uri.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            var https = Uri.UriSchemeHttps + uri[Scheme.Length..];
            JustDropAssetMap? owner;
            lock (Maps) owner = Maps.FirstOrDefault(m => m.Resolve(https) != null) ?? Maps.FirstOrDefault();
            r = owner?.Answer(method, https, range);
        }
        return r ?? new JustDropAssetMap.Response(404, "Not Found", JustDropAssetMap.Cors(), null);
    }

    /// <summary>
    /// The scheme on the view's web context. Every call runs on the GLib thread (g_idle_add) against the libraries
    /// Avalonia.Controls.WebView already loaded (ScrolllerWebKitFilter.LoadedLibrary). A request is answered off that
    /// thread and finished back on it; a body streams through a pipe, so a video is never held in memory. Needs
    /// WebKitGTK 2.36 (finish_with_response); older engines list nothing.
    /// </summary>
    private static class WebKit
    {
        private delegate IntPtr Fn1(IntPtr a);
        private delegate void Fn1Void(IntPtr a);
        private delegate void FnStr(IntPtr a, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);
        private delegate IntPtr GetOne(IntPtr headers, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        private delegate IntPtr HeadersNew(int type);
        private delegate void Append(IntPtr headers, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        private delegate void SchemeCb(IntPtr request, IntPtr data);
        private delegate void RegisterScheme(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme, SchemeCb cb, IntPtr data, IntPtr destroy);
        private delegate IntPtr ResponseNew(IntPtr stream, long length);
        private delegate void SetStatus(IntPtr response, uint status, [MarshalAs(UnmanagedType.LPUTF8Str)] string reason);
        private delegate void Fn2Void(IntPtr a, IntPtr b);
        private delegate IntPtr UnixStream(int fd, int closeFd);
        private delegate IntPtr Fn0();
        private delegate int SourceFunc(IntPtr data);
        private delegate uint IdleAdd(SourceFunc f, IntPtr data);

        [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)] private static extern int Pipe2(int[] fds, int flags);
        [DllImport("libc", EntryPoint = "close")] private static extern int Close(int fd);

        private sealed record Api(Fn1 GetContext, RegisterScheme Register, Fn1 SecurityManager, FnStr AsSecure, FnStr AsCors,
            Fn1 RequestUri, Fn1 RequestMethod, Fn1 RequestHeaders, GetOne HeaderGet, HeadersNew NewHeaders, Append HeaderAppend,
            ResponseNew NewResponse, SetStatus Status, FnStr ContentType, Fn2Void SetHeaders, Fn2Void Finish,
            Fn0 EmptyStream, UnixStream PipeStream, Fn1 Ref, Fn1Void Unref, IdleAdd Idle);

        private static readonly Lazy<Api?> Native = new(Load);
        private static readonly SchemeCb OnRequestCb = OnRequest;
        private static readonly SourceFunc OnRegisterCb = OnRegister, OnFinishCb = OnFinish;
        private static readonly HashSet<IntPtr> Contexts = new();   // GLib thread only
        private static volatile bool _registered;

        internal static bool Registered => _registered;

        private static Api? Load()
        {
            IntPtr webkit = IntPtr.Zero, soup = IntPtr.Zero;
            foreach (var name in ScrolllerWebKitFilter.Libraries) if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
            foreach (var name in new[] { "libsoup-3.0.so.0", "libsoup-2.4.so.1" }) if ((soup = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
            var glib = ScrolllerWebKitFilter.LoadedLibrary("libglib-2.0.so.0");
            var gobject = ScrolllerWebKitFilter.LoadedLibrary("libgobject-2.0.so.0");
            var gioLib = ScrolllerWebKitFilter.LoadedLibrary("libgio-2.0.so.0");
            if (webkit == IntPtr.Zero || soup == IntPtr.Zero || glib == IntPtr.Zero || gobject == IntPtr.Zero || gioLib == IntPtr.Zero) return null;
            if (!NativeLibrary.TryGetExport(webkit, "webkit_uri_scheme_request_finish_with_response", out _)) return null;   // < 2.36
            T F<T>(IntPtr lib, string sym) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, sym));
            return new Api(
                F<Fn1>(webkit, "webkit_web_view_get_context"), F<RegisterScheme>(webkit, "webkit_web_context_register_uri_scheme"),
                F<Fn1>(webkit, "webkit_web_context_get_security_manager"),
                F<FnStr>(webkit, "webkit_security_manager_register_uri_scheme_as_secure"),
                F<FnStr>(webkit, "webkit_security_manager_register_uri_scheme_as_cors_enabled"),
                F<Fn1>(webkit, "webkit_uri_scheme_request_get_uri"), F<Fn1>(webkit, "webkit_uri_scheme_request_get_http_method"),
                F<Fn1>(webkit, "webkit_uri_scheme_request_get_http_headers"), F<GetOne>(soup, "soup_message_headers_get_one"),
                F<HeadersNew>(soup, "soup_message_headers_new"), F<Append>(soup, "soup_message_headers_append"),
                F<ResponseNew>(webkit, "webkit_uri_scheme_response_new"), F<SetStatus>(webkit, "webkit_uri_scheme_response_set_status"),
                F<FnStr>(webkit, "webkit_uri_scheme_response_set_content_type"), F<Fn2Void>(webkit, "webkit_uri_scheme_response_set_http_headers"),
                F<Fn2Void>(webkit, "webkit_uri_scheme_request_finish_with_response"),
                F<Fn0>(gioLib, "g_memory_input_stream_new"), F<UnixStream>(gioLib, "g_unix_input_stream_new"),
                F<Fn1>(gobject, "g_object_ref"), F<Fn1Void>(gobject, "g_object_unref"), F<IdleAdd>(glib, "g_idle_add"));
        }

        internal static void Register(IntPtr view)
        {
            if (Native.Value is not { } api) { Log.Information("JustDropAssets: WebKitGTK without a scheme response seam, no assets"); return; }
            api.Ref(view);   // survive adapter detachment until the GLib callback
            if (api.Idle(OnRegisterCb, view) == 0) api.Unref(view);
        }

        private static int OnRegister(IntPtr view)
        {
            var api = Native.Value!;
            try
            {
                var context = api.GetContext(view);
                if (context != IntPtr.Zero && Contexts.Add(context))
                {
                    var security = api.SecurityManager(context);
                    api.AsSecure(security, Scheme);
                    api.AsCors(security, Scheme);
                    api.Register(context, Scheme, OnRequestCb, IntPtr.Zero, IntPtr.Zero);
                }
                _registered = context != IntPtr.Zero;
            }
            catch (Exception ex) { Log.Warning("JustDropAssets: scheme registration failed: {E}", ex.Message); }
            finally { api.Unref(view); }
            return 0;
        }

        private sealed record Pending(IntPtr Request, JustDropAssetMap.Response Answer, int ReadFd);

        private static void OnRequest(IntPtr request, IntPtr data)
        {
            var api = Native.Value!;
            try
            {
                var uri = Marshal.PtrToStringUTF8(api.RequestUri(request));
                var method = Marshal.PtrToStringUTF8(api.RequestMethod(request));
                var headers = api.RequestHeaders(request);
                var range = headers == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(api.HeaderGet(headers, "Range"));
                api.Ref(request);
                _ = Task.Run(() => Compose(api, request, method, uri, range));
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: scheme request failed: {E}", ex.Message); }
        }

        /// <summary>Off the GLib thread: the answer, its body pumped into a pipe, then the finish queued back.</summary>
        private static void Compose(Api api, IntPtr request, string? method, string? uri, string? range)
        {
            JustDropAssetMap.Response answer;
            try { answer = Answer(method, uri, range); }
            catch (Exception ex) { Log.Debug("JustDropAssets: answer failed: {E}", ex.Message); answer = new(500, "Internal Server Error", JustDropAssetMap.Cors(), null); }
            int readFd = -1;
            if (answer.Body is { } body)
            {
                var fds = new int[2];
                if (Pipe2(fds, 0x80000 /* O_CLOEXEC */) != 0) { body.Dispose(); answer = answer with { Status = 500, Reason = "Internal Server Error", Body = null }; }
                else
                {
                    readFd = fds[0];
                    _ = Task.Factory.StartNew(() => Pump(body, fds[1]), TaskCreationOptions.LongRunning);
                }
            }
            var state = GCHandle.Alloc(new Pending(request, answer, readFd));
            if (api.Idle(OnFinishCb, GCHandle.ToIntPtr(state)) != 0) return;
            state.Free();
            if (readFd >= 0) Close(readFd);
            api.Unref(request);
        }

        /// <summary>Streams the body into the pipe; the engine closing its end (a seek, a closed page) ends it quietly.</summary>
        private static void Pump(Stream body, int writeFd)
        {
            try
            {
                using var pipe = new FileStream(new SafeFileHandle((IntPtr)writeFd, ownsHandle: true), FileAccess.Write, 1);
                body.CopyTo(pipe, 64 * 1024);
            }
            catch (IOException) { }
            catch (Exception ex) { Log.Debug("JustDropAssets: body stream failed: {E}", ex.Message); }
            finally { body.Dispose(); }
        }

        private static int OnFinish(IntPtr data)
        {
            var api = Native.Value!;
            var state = GCHandle.FromIntPtr(data);
            var p = (Pending)state.Target!;
            state.Free();
            try
            {
                var stream = p.ReadFd >= 0 ? api.PipeStream(p.ReadFd, 1) : api.EmptyStream();
                long length = -1;
                var headers = api.NewHeaders(1 /* SOUP_MESSAGE_HEADERS_RESPONSE */);
                foreach (var line in p.Answer.Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out length);
                    api.HeaderAppend(headers, name, value);
                }
                var response = api.NewResponse(stream, p.ReadFd >= 0 ? length : 0);
                api.Status(response, (uint)p.Answer.Status, p.Answer.Reason);
                if (ContentTypeOf(p.Answer.Headers) is { } type) api.ContentType(response, type);
                api.SetHeaders(response, headers);   // transfer full
                api.Finish(p.Request, response);
                api.Unref(response);
                api.Unref(stream);
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: scheme finish failed: {E}", ex.Message); }
            finally { api.Unref(p.Request); }
            return 0;
        }
    }

    internal static string? ContentTypeOf(string headers) =>
        headers.Split("\r\n").FirstOrDefault(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))?["Content-Type:".Length..].Trim();

    // ============================== WebView2 ==============================

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SupportedOSPlatform("windows")]
    private static void Windows(IntPtr pointer, JustDropAssetMap map)
    {
        var core = CoreWebView2.CreateFromComICoreWebView2(pointer);
        core.AddWebResourceRequestedFilter(JustDropAssetMap.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                var uri = e.Request.Uri;
                // Every filter on this core raises this event (the Scrolller gate, the sign-in header): our host only.
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.Equals(u.Host, JustDropAssetMap.Host, StringComparison.OrdinalIgnoreCase)) return;
                var method = e.Request.Method;
                string? range = null;
                try { range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null; } catch { }
                var deferral = e.GetDeferral();
                _ = Serve(core, e, deferral, () => map.Answer(method, uri, range));
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: request hook failed: {E}", ex.Message); }
        };
    }

    [SupportedOSPlatform("windows")]
    private static async Task Serve(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral,
        Func<JustDropAssetMap.Response?> answer)
    {
        try
        {
            if (await Task.Run(answer) is { } r)
                e.Response = core.Environment.CreateWebResourceResponse(r.Body, r.Status, r.Reason, r.Headers);
        }
        catch (Exception ex) { Log.Debug("JustDropAssets: answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }
}
