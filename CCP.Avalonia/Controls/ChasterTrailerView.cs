// PORTED from ConditioningControlPanel/Controls/ChasterTrailerView.cs at f7be538c7.
// One browser for every menu row; the host keeps its still picture until the latest scene mounts.
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
namespace ConditioningControlPanel.Avalonia.Controls;
public sealed class ChasterTrailerView : Grid
{
    private WebHost? _web;
    private Uri? _page;
    private Func<string, Task<string?>>? _script;
    private bool _started, _disposed, _mounting, _paused;
    private int _version;
    private (string Id, int? Add, int? Sub)? _pending;
    public bool IsReady { get; private set; }
    public bool HasFailed { get; private set; }
    public event EventHandler? Ready;
    public event EventHandler<string>? Failed;
    // Capture the page's current stage so closing the card stops its scene, including WAAPI loops.
    private const string PrepareScript = "(function(){if(!window.__ready)return false;"
        + "if(!window.__ccpTrailerRun){window.__ccpTrailerRun=CT.run;CT.run=function(){"
        + "return window.__ccpTrailerStage=window.__ccpTrailerRun.apply(CT,arguments);};}"
        + "document.addEventListener('contextmenu',function(e){e.preventDefault();});return true;})()";
    internal const string PauseScript = "(function(){var s=window.__ccpTrailerStage;if(s)s.dead=true;"
        + "document.getAnimations().forEach(function(a){a.cancel();});})()";
    public ChasterTrailerView() => Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0C, 0x1F));
    // No native browser or real profile in tests; only the asynchronous page protocol is replaced.
    internal ChasterTrailerView(Uri page, Func<string, Task<string?>> script) : this() => (_page, _script) = (page, script);
    public void Start()
    {
        if (_started || _disposed || HasFailed) return;
        _started = true;
        if (_script != null) return;
        try
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "chaster", "trailers.html")))
            { Fail("trailers page missing"); return; }
            _web = new WebHost(suppressNewWindows: true);
            _web.EnvironmentRequested += ConfigureBrowser;
            if (!_web.HasEngine) { _web = null; Fail("browser unavailable"); return; }
            _page = new Uri(WebAssetServer.Shared.Url("chaster/trailers.html"));
            _script = _web.InvokeScriptAsync;
            _web.AllowNavigation = AllowsNavigation;
            ObserveBrowser(_web);
            Children.Add(_web);
            _web.Navigate(_page);
        }
        catch (Exception) { Fail("browser initialization failed"); }
    }
    internal static void ConfigureBrowser(WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        if (e is WindowsWebView2EnvironmentRequestedEventArgs win)
        {
            win.UserDataFolder = Path.Combine(CorePaths.UserData, "browser_data_chaster");
            win.AdditionalBrowserArguments = "--disable-direct-composition-video-overlays "
                + "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";
        }
        if (e is GtkWebViewEnvironmentRequestedEventArgs gtk) gtk.EphemeralDataManager = true;
    }
    internal bool AllowsNavigation(Uri uri) => _page != null && uri.Scheme == _page.Scheme
        && uri.Host == _page.Host && uri.Port == _page.Port && uri.AbsolutePath.StartsWith("/chaster/", StringComparison.Ordinal);
    internal static string MountScript(string id, int? add = null, int? sub = null)
    {
        var safe = (id ?? "").Replace("\\", "").Replace("'", "").Replace("\"", "").Replace("<", "").Replace(">", "");
        static string Num(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "null";
        return "window.__mount && window.__mount('" + safe + "'," + Num(add) + "," + Num(sub) + ")";
    }
    public void Show(string id, int? add = null, int? sub = null)
    {
        if (_disposed || HasFailed) return;
        _pending = (id, add, sub);
        _version++;
        _paused = false;
        Start();
        if (IsReady) _ = MountPending();
    }
    internal async Task NavigationFinished(Uri? uri, bool success)
    {
        if (_disposed || HasFailed) return;
        IsReady = false;
        if (!success || uri == null || !AllowsNavigation(uri)) { Fail("navigation failed"); return; }
        try
        {
            if (!True(await _script!(PrepareScript))) { Fail("trailers page not ready"); return; }
            await MountPending();
        }
        catch (Exception) { Fail("trailers page failed"); }
    }
    private static bool True(string? result) => string.Equals(result?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase);
    private async Task MountPending()
    {
        if (_mounting || _disposed || HasFailed) return;
        _mounting = true;
        try
        {
            int mounted;
            do
            {
                mounted = _version;
                if (!_paused && _pending is { } p && !True(await _script!(MountScript(p.Id, p.Add, p.Sub))))
                { Fail("scene mount failed"); return; }
            } while (mounted != _version && !_disposed && !HasFailed && !_paused);
            if (_disposed || HasFailed) return;
            if (_paused) await _script!(PauseScript);
            IsReady = true;
            Ready?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception) { Fail("scene mount failed"); }
        finally { _mounting = false; }
    }
    internal IDisposable? PreserveForMove() => _web?.PreserveForMove();
    public void Pause()
    {
        _paused = true;
        IsVisible = false;
        if (IsReady && _script != null) _ = _script(PauseScript);
    }
    internal void ObserveBrowser(WebHost web)
    {
        web.NavigationFinished += (uri, ok) => _ = NavigationFinished(uri, ok);
        web.BrowserProcessFailed += BrowserProcessFailed;
    }
    internal void BrowserProcessFailed() => Fail("browser process failed");
    private void Fail(string reason)
    {
        if (_disposed || HasFailed) return;
        HasFailed = true;
        IsReady = false;
        Children.Clear();
        _web = null;
        Failed?.Invoke(this, reason);
    }
    public void Shutdown()
    {
        if (_disposed) return;
        Pause();
        _disposed = true;
        IsReady = false;
        Children.Clear();
        _web = null;
    }
}
