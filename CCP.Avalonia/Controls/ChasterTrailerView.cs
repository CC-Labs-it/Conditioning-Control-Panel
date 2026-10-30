// WPF Controls/ChasterTrailerView.cs:84-226; queue/scripts from 7cf2cee4dcfa, base WebHost network gates retained.
using System;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
namespace ConditioningControlPanel.Avalonia.Controls;
public sealed class ChasterTrailerView : Grid
{
    internal WebHost? Browser { get; set; }
    internal Uri? Page { get; private set; }
    private bool _started, _disposed, _mounting, _paused;
    private int _version;
    private (string Id, int? Add, int? Sub)? _pending;
    public bool IsReady { get; private set; }
    public bool HasFailed { get; private set; }
    public event EventHandler? Ready;
    public event EventHandler? Failed;
    private const string PrepareScript = "(function(){if(!window.__ready)return false;"
        + "if(!window.__ccpTrailerRun){window.__ccpTrailerRun=CT.run;CT.run=function(){"
        + "return window.__ccpTrailerStage=window.__ccpTrailerRun.apply(CT,arguments);};}return true;})()";
    internal const string PauseScript = "(function(){var s=window.__ccpTrailerStage;if(s)s.dead=true;"
        + "document.getAnimations().forEach(function(a){a.cancel();});})()";
    private void Start()
    {
        if (_started || _disposed || HasFailed) return;
        _started = true;
        try
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "chaster", "trailers.html"))) { Fail(); return; }
            Browser ??= new WebHost();
            if (!Browser.HasEngine) { Fail(); return; }
            Page = new Uri(WebAssetServer.Shared.Url("chaster/trailers.html"));
            Browser.AllowNavigation = uri => uri == Page;
            Browser.SuppressNewWindows = true;
            Browser.EnvironmentRequested += ConfigureBrowser;
            Browser.NavigationFinished += (uri, ok) => _ = NavigationFinished(uri, ok);
            Children.Add(Browser);
            Browser.Navigate(Page);
        }
        catch (Exception) { Fail(); }
    }
    internal static void ConfigureBrowser(WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        if (e is WindowsWebView2EnvironmentRequestedEventArgs win)
        {
            win.UserDataFolder = Path.Combine(CorePaths.UserData, "browser_data_chaster");
            win.AdditionalBrowserArguments = "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";
        }
        if (e is GtkWebViewEnvironmentRequestedEventArgs gtk) gtk.EphemeralDataManager = true;
    }
    internal static string MountScript(string id, int? add = null, int? sub = null)
    {
        var safe = id.Replace("\\", "").Replace("'", "").Replace("\"", "").Replace("<", "").Replace(">", "");
        static string Num(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "null";
        return "window.__mount && window.__mount('" + safe + "'," + Num(add) + "," + Num(sub) + ")";
    }
    public void Show(string id, int? add, int? sub)
    {
        if (_disposed || HasFailed) return;
        _pending = (id, add, sub); _version++; _paused = false;
        Start();
        if (IsReady) _ = MountPending();
    }
    internal async Task NavigationFinished(Uri? uri, bool success)
    {
        if (_disposed || HasFailed) return;
        IsReady = false;
        if (!success || uri != Page) { Fail(); return; }
        if (!True(await Browser!.InvokeScriptAsync(PrepareScript))) { Fail(); return; }
        await MountPending();
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
                if (!_paused && _pending is { } p && !True(await Browser!.InvokeScriptAsync(MountScript(p.Id, p.Add, p.Sub)))) { Fail(); return; }
            } while (mounted != _version && !_disposed && !HasFailed && !_paused);
            if (_disposed || HasFailed) return;
            if (_paused) await Browser!.InvokeScriptAsync(PauseScript);
            IsReady = true; Ready?.Invoke(this, EventArgs.Empty);
        }
        finally { _mounting = false; }
    }
    internal IDisposable? PreserveForMove() => Browser?.PreserveForMove();
    public void Pause() { _paused = true; IsVisible = false; if (IsReady) _ = Browser?.InvokeScriptAsync(PauseScript); }
    private void Fail() { if (_disposed || HasFailed) return; HasFailed = true; IsReady = false; IsVisible = false; Children.Clear(); Failed?.Invoke(this, EventArgs.Empty); }
    public void Shutdown() { Pause(); _disposed = true; IsReady = false; Children.Clear(); }
}
