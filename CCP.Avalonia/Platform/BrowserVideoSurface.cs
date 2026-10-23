using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Video.Browser;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>One owned browser-video session, using the same GTK/XWayland capability as WebHost.
/// No native provider, unmapped file, pinned audio sink or competing session means LibVLC.</summary>
internal sealed class BrowserVideoSurface : IDisposable
{
    private static object? _owner;
    internal static bool SessionActive => _owner != null;
    internal static Func<BrowserVideoSurface?> Create = () =>
    {
        try
        {
            var surface = new BrowserVideoSurface();
            if (surface.View.HasEngine) return surface;
            surface.Dispose();
        }
        catch { /* Adapter construction is an availability failure: keep the LibVLC path. */ }
        return null;
    };

    internal static void Seed() => BrowserVideoGate.EngineAccepts = (path, key) =>
        WebHost.IsAvailable && !BrowserUnsafeVideoCache.Contains(path, key) && Map(path) != null;

    internal static string? Map(string path)
    {
        try
        {
            var server = WebAssetServer.Shared;
            var root = server.AssetsRoot?.Invoke();
            if (string.IsNullOrEmpty(root)) return null;
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
            var assetPath = WebAssetServer.AssetPath(relative);
            if (server.ResolveFile(assetPath) == null) return null;
            return server.Url(assetPath);
        }
        catch { return null; }
    }

    internal static bool TryAcquire(object owner, string path)
    {
        if (_owner != null || !BrowserVideoGate.ShouldUseBrowser(path)) return false;
        _owner = owner; // All acquisition/release is on the UI thread, before creating any surface.
        return true;
    }

    internal static void Release(object owner)
    {
        if (ReferenceEquals(_owner, owner)) _owner = null;
    }

    internal WebHost View { get; } = new();
    internal event Action<JsonElement>? Message;
    internal event Action? Failed;
    internal bool Playing { get; private set; }
    internal double Volume { get; private set; }
    private readonly Func<string, Task<string?>> _execute;
    private readonly string _id = Guid.NewGuid().ToString("N");
    // ponytail: WebHost has no process-exit event; startup and game safety deadlines bound a silent renderer failure.
    private DispatcherTimer? _startup;
    private bool _closed, _loaded;
    private string _url = "";

    // The script seam lets headless tests delay a real async completion without constructing a native adapter.
    internal BrowserVideoSurface(Func<string, Task<string?>>? execute = null)
    {
        _execute = execute ?? View.InvokeScriptAsync;
        View.AllowNavigation = uri => uri.ToString() == "about:blank";
        View.NavigationCompleted += Navigated;
        View.WebMessage += Receive;
    }

    internal void Start(string url, bool primary, int masterVolume)
    {
        _url = url;
        Volume = primary ? Math.Clamp(masterVolume / 100.0, 0, 1) : 0;
        _startup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _startup.Tick += (_, _) => Fail();
        _startup.Start();
        View.Navigate(new Uri("about:blank"));
    }

    private async void Navigated(Uri uri)
    {
        if (_closed || _loaded || uri.ToString() != "about:blank") return;
        _loaded = true;
        try
        {
            // Install after navigation: WebHost's string carrier exists then on both GTK and WebView2.
            var result = await _execute(BuildScript(_url, Volume, _id));
            if (_closed) return;
            if (result != "true") { Fail(); return; }
            if (!Playing && _startup != null) _startup.Interval = TimeSpan.FromSeconds(10);
        }
        catch { if (!_closed) Fail(); }
    }

    internal void Receive(string body)
    {
        if (_closed || !_loaded) return;
        try
        {
            using var json = JsonDocument.Parse(body);
            var message = json.RootElement;
            if (message.GetProperty("session").GetString() != _id) return;
            switch (message.GetProperty("type").GetString())
            {
                case "playing": Playing = true; _startup?.Stop(); break;
                case "error": Fail(); return;
            }
            Message?.Invoke(message);
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (System.Collections.Generic.KeyNotFoundException) { }
    }

    internal string SessionId => _id;
    private void Fail()
    {
        if (_closed) return;
        _startup?.Stop();
        Failed?.Invoke();
    }

    internal async void Pause()
    {
        if (_closed) return;
        try { await _execute("document.querySelector('video')?.pause();"); }
        catch { /* Teardown still detaches the adapter if its script channel has died. */ }
    }

    public void Dispose()
    {
        if (_closed) return;
        Pause();
        _closed = true;
        _startup?.Stop();
        _startup = null;
        View.NavigationCompleted -= Navigated;
        View.WebMessage -= Receive;
        Message = null;
        Failed = null;
        // Detaching the native control releases its adapter; callbacks already in flight are fenced above.
        View.Content = null;
    }

    internal static string BuildScript(string url, double volume, string id) =>
        "(function(c){" + """
        const send=m=>{m.session=c.id; const s=JSON.stringify(m);
          if(typeof window.invokeCSharpAction==='function') window.invokeCSharpAction(s);
          else if(window.chrome?.webview) window.chrome.webview.postMessage(s);};
        document.body.style='margin:0;background:black;overflow:hidden';
        const v=document.createElement('video'); v.playsInline=true; v.preload='auto';
        v.style='width:100vw;height:100vh;object-fit:contain';
        v.muted=c.volume===0; v.volume=c.volume; document.body.replaceChildren(v);
        const meta=()=>send({type:'meta',durationMs:Number.isFinite(v.duration)?v.duration*1000:0});
        v.onloadedmetadata=meta; v.ondurationchange=meta;
        v.onplaying=()=>send({type:'playing'}); v.onended=()=>send({type:'ended'});
        v.onerror=()=>send({type:'error'});
        document.addEventListener('pointerdown',()=>send({type:'pointer'}),true);
        window.addEventListener('keydown',e=>{e.preventDefault(); if(!e.repeat)
          send({type:'key',key:e.key,alt:e.altKey});},true);
        v.src=c.url; v.play().catch(()=>send({type:'error'})); return true;
        """ + "})(" + JsonSerializer.Serialize(new { url, volume, id }) + ");";
}
