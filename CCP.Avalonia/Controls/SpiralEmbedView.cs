using System;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Platform;
using Serilog;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>The Spiral-only ready/state handshake over the shared native WebHost carrier.</summary>
    public sealed class SpiralEmbedView : Grid, IDisposable
    {
        private readonly string _mode;
        private readonly SpiralEmbedBridge _bridge = new();
        private WebHost? _web;
        private bool _started, _disposed;
        public bool IsReady => _bridge.IsReady;
        public bool HasFailed { get; private set; }
        public string Url => SpiralEmbedBridge.BuildUrl(_mode);
        public event EventHandler? Ready;
        public event EventHandler? Navigated;
        public event EventHandler<string>? Failed;

        // Headless tests exercise exactly the script payload sent to WebHost without a browser.
        internal Func<string, Task>? SendJson { get; set; }
        internal WebHost? Host => _web;
        internal Func<WebHost>? CreateHost { get; set; }

        public SpiralEmbedView(string mode)
        {
            _mode = mode == "map" ? "map" : "mini";
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x36));
        }

        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;
            try
            {
                _web = CreateHost?.Invoke() ?? new WebHost();
                _web.Background = Background;
                _web.AllowNavigation = SpiralEmbedBridge.AllowsNavigation;
                _web.InjectCarrier = true;
                _web.ConfigureWindowsCore = core => Platform.SpiralWebView2Policy.Attach(core, _web.OnBrowserProcessFailed);
                _web.BrowserProcessFailed += OnBrowserProcessFailed;
                _web.EnvironmentRequested += OnEnvironmentRequested;
                _web.WebMessage += Receive;
                _web.NavigationFinished += OnNavigated;
                Children.Add(_web);
                if (!_web.HasEngine)
                {
                    Fail(WebHost.UnavailableReason ?? "browser unavailable");
                    return;
                }
                _web.Source = new Uri(Url);
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        // WPF SpiralEmbedView.InitAsync: one profile for mini/map, devtools off, and the
        // anti-occlusion switches that keep a rail-docked canvas painting on Windows.
        private void OnEnvironmentRequested(WebViewEnvironmentRequestedEventArgs e)
        {
            try { ConfigureEnvironment(e); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        internal static void ConfigureEnvironment(WebViewEnvironmentRequestedEventArgs e)
        {
            e.EnableDevTools = false;
            var folder = Path.Combine(CorePaths.UserData, "browser_data_spiral");
            Directory.CreateDirectory(folder);
            switch (e)
            {
                case WindowsWebView2EnvironmentRequestedEventArgs win:
                    win.UserDataFolder = folder;
                    win.AdditionalBrowserArguments =
                        "--disable-direct-composition-video-overlays " +
                        "--disable-features=CalculateNativeWinOcclusion " +
                        "--disable-backgrounding-occluded-windows";
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.BaseDataDirectory = folder;
                    gtk.BaseCacheDirectory = Path.Combine(folder, "cache");
                    break;
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                    wpe.DataDirectory = folder;
                    wpe.CacheDirectory = Path.Combine(folder, "cache");
                    break;
            }
            // Windows settings and process failure use the native core before its first navigation.
            // WebKit document-created carrier and process failure remain unported.
        }

        public void PostState(DescentBlock? block)
        {
            if (_disposed) return;
            try
            {
                var state = SpiralEmbedBridge.BuildState(block,
                    AmbientFxCanvas.Env.Level != MotionLevel.Full, AmbientFxCanvas.Env.CurrentTier);
                if (_bridge.Post(state.ToString(Formatting.None)) is { } message) _ = Send(message);
            }
            catch (Exception ex) { Log.Debug("[Spiral] PostState: {Error}", ex.Message); }
        }

        internal void Receive(string message)
        {
            if (_disposed) return;
            try
            {
                var wasReady = IsReady;
                if (_bridge.Receive(message) is { } latest) _ = Send(latest);
                if (IsReady && !wasReady) Ready?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { Log.Debug("[Spiral] OnWebMessageReceived: {Error}", ex.Message); }
        }

        private async Task Send(string message)
        {
            try { await (SendJson?.Invoke(message) ?? _web?.PostJsonAsync(message) ?? Task.CompletedTask); }
            catch (Exception ex) { Log.Debug("[Spiral] PostState: {Error}", ex.Message); }
        }

        internal void OnNavigated(Uri url, bool success)
        {
            if (_disposed || !SpiralEmbedBridge.AllowsNavigation(url)) return;
            if (!success)
            {
                Fail("navigation failed");
                return;
            }
            try { Navigated?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Debug("[Spiral] Navigated handler threw: {Error}", ex.Message); }
        }

        internal void OnBrowserProcessFailed(string reason) => Fail("browser process failed: " + reason);

        private void Fail(string reason)
        {
            if (_disposed) return;
            HasFailed = true;
            Dispose();
            try { Failed?.Invoke(this, reason); }
            catch (Exception ex) { Log.Debug("[Spiral] Failed handler threw: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bridge.Reset();
            if (_web is { } web)
            {
                _web = null;
                web.BrowserProcessFailed -= OnBrowserProcessFailed;
                web.ConfigureWindowsCore = null;
                web.EnvironmentRequested -= OnEnvironmentRequested;
                web.WebMessage -= Receive;
                web.NavigationFinished -= OnNavigated;
                try { web.Source = null; }
                catch (Exception ex) { Log.Debug("[Spiral] dispose: {Error}", ex.Message); }
                finally { Children.Remove(web); }
            }
        }
    }
}
