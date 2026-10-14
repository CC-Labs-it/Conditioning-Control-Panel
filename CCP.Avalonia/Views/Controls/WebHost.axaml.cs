using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// A <see cref="NativeWebView"/> that degrades to a legible panel instead of an empty
    /// rectangle when the machine has no web engine.
    ///
    /// This is NOT a ported view. The WPF head talks to Microsoft.Web.WebView2 directly
    /// (ChaosWebViewHost, MainWindow.Browser, SpiralEmbedView) and that package is Windows-only;
    /// Avalonia.Controls.WebView is the cross-platform replacement - WebView2 on Windows,
    /// WebKitGTK or WPE WebKit on Linux - and this control is the single place the head touches
    /// it, so those three hosts have one seam to move onto rather than three.
    ///
    /// Beyond <see cref="Source"/> it surfaces the two things a hosted page has to be driven and
    /// fenced with: <see cref="AllowNavigation"/> (NativeWebView.NavigationStarted, whose args
    /// carry a settable Cancel — so a per-navigation allowlist that also catches redirects is
    /// possible here, unlike what the first pass of these views assumed) and
    /// <see cref="InvokeScriptAsync"/>, plus the live URL (<see cref="CurrentUrl"/>,
    /// <see cref="NavigationCompleted"/>). NativeWebView additionally offers WebMessageReceived,
    /// NewWindowRequested, GoBack/GoForward, Stop and Refresh; nothing needs
    /// them yet, so they are not wrapped. It has NO zoom factor, no document-created script
    /// injection, and no fullscreen-element signal.
    ///
    /// The whole point is the fallback. A Linux box without webkit2gtk installed is the DEFAULT,
    /// not the exception, and a web view with no engine paints nothing: the render proof would
    /// show a blank region and pass. So availability is probed BEFORE anything is constructed, and
    /// the real control is only ever added to the tree when an engine can actually host it.
    /// </summary>
    public partial class WebHost : UserControl
    {
        /// <summary>Page to load. Mirrors <see cref="NativeWebView.SourceProperty"/>.</summary>
        public static readonly StyledProperty<Uri?> SourceProperty =
            AvaloniaProperty.Register<WebHost, Uri?>(nameof(Source));

        public Uri? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

        /// <summary>
        /// Per-navigation gate. Return false to cancel. Runs for EVERY navigation the engine
        /// starts — the first one and every redirect, in-page link and script-driven hop after it —
        /// which is what makes it the replacement for WebView2's <c>NavigationStarting</c> allowlist
        /// rather than a one-shot check on <see cref="Source"/>.
        ///
        /// Set it BEFORE assigning <see cref="Source"/>: the gate is read at navigation time, and a
        /// Source assigned first can start navigating before the predicate is in place.
        /// Null means "allow everything", which is the right default for a host that is only ever
        /// pointed at pages the app itself chose.
        /// </summary>
        public Func<Uri, bool>? AllowNavigation { get; set; }

        /// <summary>
        /// The URL the page actually sits on after its last completed navigation: redirects,
        /// in-page links and script hops included, unlike <see cref="Source"/>, which is only
        /// what a caller last asked for. Null until a navigation completes. WPF's
        /// <c>BrowserService.GetCurrentUrl</c>.
        /// </summary>
        public Uri? CurrentUrl { get; private set; }

        /// <summary>
        /// Raised on the UI thread with <see cref="CurrentUrl"/> after every top-level navigation
        /// completes, failed ones included, as WPF's <c>BrowserService.NavigationCompleted</c>
        /// does (BrowserService.cs:1435-1443) - the live-URL signal MainWindow.Browser.cs:132
        /// hangs its status line and catalogue lookup on.
        /// </summary>
        public event Action<Uri>? NavigationCompleted;

        /// <summary>
        /// The seam the engine's NavigationCompleted routes through, internal so a headless test
        /// (which never has an engine) can drive the same path. A completion with no URL raises
        /// nothing: there is nothing live to report.
        /// </summary>
        internal void OnNavigationCompleted(Uri? url)
        {
            if (url is null) return;
            CurrentUrl = url;
            if (InjectCarrier) _ = InvokeScriptAsync(CarrierShim);
            NavigationCompleted?.Invoke(url);
        }

        /// <summary>
        /// A string the page posted (<c>window.invokeCSharpAction</c>, which the engine injects; it
        /// JSON-stringifies objects). The stand-in for WPF's <c>CoreWebView2.WebMessageReceived</c>.
        /// </summary>
        public event Action<string>? WebMessage;

        /// <summary>
        /// One extra request header for a url, or null (the usual answer). The stand-in for WPF's
        /// <c>AddWebResourceRequestedFilter</c> + <c>Request.Headers.SetHeader</c> (JustDropHostService.AttachAuthHeader):
        /// the caller's rule decides, per request, whether its credential rides along. Never logged.
        /// </summary>
        public Func<Uri, (string Name, string Value)?>? RequestHeader { get; set; }

        /// <summary>Requests <see cref="RequestHeader"/> actually stamped (the engine accepted the header; never the value).</summary>
        internal int StampedRequests { get; private set; }

        private void StampRequestHeader(WebResourceRequestedEventArgs e)
        {
            try
            {
                if (e.Request?.Uri is { } uri) Stamp(uri, e.Request.Headers.TrySet);
            }
            catch (Exception ex) { Log.Debug("WebHost: request header not set: {Error}", ex.Message); }
        }

        /// <summary>One request through <see cref="RequestHeader"/>; counts only a header the engine took (tests drive it).</summary>
        internal bool Stamp(Uri uri, Func<string, string, bool> trySet)
        {
            if (RequestHeader?.Invoke(uri) is not { } header || !trySet(header.Name, header.Value)) return false;
            StampedRequests++;
            return true;
        }

        /// <summary>
        /// The shared page carrier, for any page this head hosts (local games, remote sites). Pages
        /// written for WebView2 talk through <c>window.chrome.webview</c> (postMessage out, a
        /// <c>message</c> event in); WebKitGTK injects only <c>window.invokeCSharpAction</c> (strings).
        /// <see cref="CarrierShim"/> gives such a page a <c>chrome.webview</c> over that string
        /// channel (and <c>__ccpRnPush</c> back), and is a no-op where a real one exists (WebView2).
        /// With <see cref="InjectCarrier"/> it runs after every completed navigation: the engine has
        /// no document-created script seam, so a page that probes the carrier only at load misses it.
        /// </summary>
        internal const string CarrierShim =
            "(function(){var w=window;if(w.chrome&&w.chrome.webview&&w.chrome.webview.postMessage)return;"
            + "if(typeof w.invokeCSharpAction!=='function')return;var t=new EventTarget();"
            + "t.postMessage=function(m){w.invokeCSharpAction(typeof m==='string'?m:JSON.stringify(m));};"
            + "w.chrome=w.chrome||{};w.chrome.webview=t;"
            + "if(typeof w.__ccpRnPush!=='function')w.__ccpRnPush=function(j){var m;try{m=JSON.parse(j);}catch(e){return;}"
            + "t.dispatchEvent(new MessageEvent('message',{data:m}));};})();";

        /// <summary>Run <see cref="CarrierShim"/> after each completed navigation (see there).</summary>
        public bool InjectCarrier { get; set; }

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post): the page's string push, else a message
        /// event on <c>chrome.webview</c>. Every hosted page shares this one carrier script.</summary>
        public Task<string?> PostJsonAsync(string json) => InvokeScriptAsync(
            "(function(m){try{if(typeof window.__ccpRnPush==='function'){window.__ccpRnPush(JSON.stringify(m));return;}"
            + "var w=window.chrome&&window.chrome.webview;if(w&&w.dispatchEvent){w.dispatchEvent(new MessageEvent('message',{data:m}));}}catch(e){}})("
            + json + ");");

        /// <summary>The seam the engine's WebMessageReceived routes through (headless tests drive it).</summary>
        internal void OnWebMessage(string? body)
        {
            if (!string.IsNullOrEmpty(body)) WebMessage?.Invoke(body);
        }

        /// <summary>
        /// True when THIS instance built an adapter. <see cref="IsAvailable"/> is the process-wide
        /// probe; the constructor can still fail after it passes, and a caller about to drive the
        /// page through script needs to know about this control, not about the machine.
        /// </summary>
        public bool HasEngine => _web is not null;

        /// <summary>
        /// Runs JS in the current page and returns its result, or null when there is no engine.
        /// The stand-in for <c>CoreWebView2.ExecuteScriptAsync</c>.
        ///
        /// The two adapters do NOT agree on the return shape: WebView2 hands back a JSON literal
        /// (a string result arrives quoted), WebKitGTK hands back the raw value. Callers that read
        /// the result must tolerate both — see <c>EnhancementPlayerWindow.Unquote</c>.
        ///
        /// There is no equivalent of <c>AddScriptToExecuteOnDocumentCreatedAsync</c>, so anything
        /// that must be present before the page's own scripts run has no seam here.
        /// </summary>
        public async Task<string?> InvokeScriptAsync(string javaScript)
        {
            if (_web is null || string.IsNullOrEmpty(javaScript)) return null;
            try { return await _web.InvokeScript(javaScript); }
            catch (Exception ex)
            {
                Log.Debug("WebHost: InvokeScript failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// True when some adapter is both installed and able to host a native control. Probed once
        /// per process: <see cref="WebViewAdapterInfo.GetAdapterInfo"/> shells out to the platform
        /// (dlopen of libwebkit2gtk, a WebView2 registry lookup) and the answer cannot change while
        /// the app runs.
        /// </summary>
        public static bool IsAvailable => UnavailableReason is null;

        private static readonly Lazy<string?> _reason = new(ProbeUnavailableReason);

        /// <summary>Null when a web view can be hosted; otherwise the platform's own explanation.</summary>
        public static string? UnavailableReason => _reason.Value;

        private static readonly Lazy<(string Text, string? Command)?> _linuxHint = new(() =>
            ConditioningControlPanel.Services.LinuxDependencies.Check(
                ConditioningControlPanel.Services.LinuxDependencies.All.Where(d => d.Need.StartsWith("web views"))));

        private readonly Panel _webSlot;
        private readonly Border _fallback;
        private readonly TextBlock _txtReason, _txtSource;
        private readonly NativeWebView? _web;

        public WebHost()
        {
            AvaloniaXamlLoader.Load(this);
            _webSlot = this.FindControl<Panel>("WebSlot")!;
            _fallback = this.FindControl<Border>("Fallback")!;
            _txtReason = this.FindControl<TextBlock>("TxtReason")!;
            _txtSource = this.FindControl<TextBlock>("TxtSource")!;

            if (IsAvailable)
            {
                // try/catch, not a bare new: the probe says an engine is installed, it does not
                // promise the adapter builds. A throw here must show the panel, not kill the host.
                try
                {
                    _web = new NativeWebView();
                    // CCP_LINUX_WEBVIEW=gtk: skip WPE, which the package would otherwise pick first.
                    if (LinuxOptIn == "gtk")
                        _web.EnvironmentRequested += (_, e) =>
                        {
                            if (e is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe) wpe.PreferWebKitGtkInstead = true;
                        };
                    // Subscribed once, here, rather than when a caller sets AllowNavigation: the
                    // gate has to be live for the FIRST navigation too, and a caller that assigns
                    // the predicate and the Source in that order would otherwise race the engine.
                    _web.NavigationStarted += OnNavigationStarted;
                    _web.NavigationCompleted += (_, e) => OnNavigationCompleted(e.Request ?? _web?.Source);
                    _web.WebMessageReceived += (_, e) => OnWebMessage(e.Body);
                    _web.WebResourceRequested += (_, e) => StampRequestHeader(e);
                    _webSlot.Children.Add(_web);
                }
                catch (Exception ex)
                {
                    _web = null;
                    _txtReason.Text = ex.Message;
                }
            }
            else
            {
                // Linux: name the distro's install command (docs/avalonia-linux-install.md) instead of
                // the adapter's generic "Install webkit2gtk 4.0+ package.", which stays the fallback.
                var hint = _linuxHint.Value;
                _txtReason.Text = hint?.Text ?? UnavailableReason!;
                if (hint?.Command is { } command)
                {
                    var copy = this.FindControl<Button>("BtnCopyCommand")!;
                    copy.IsVisible = true;
                    copy.Click += async (_, _) =>
                    {
                        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) await c.SetTextAsync(command); }
                        catch (Exception ex) { Log.Debug("WebHost: copy failed: {Error}", ex.Message); }
                    };
                }
            }

            _fallback.IsVisible = _web is null;
            ApplySource();
        }

        private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            var target = e.Request;
            // A CCP_USERDATA_DIR sandbox never loads a real site, whatever the caller's gate says.
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(target)) { e.Cancel = true; return; }
            var gate = AllowNavigation;
            if (gate is null) return;
            // No URL to judge: refuse. A navigation the gate cannot see is exactly the one a
            // hostile page would use to slip past it.
            if (target is null) { e.Cancel = true; return; }
            if (gate(target)) return;
            e.Cancel = true;
            // Host + path only: a signed media URL's query string must not reach the log.
            Log.Warning("WebHost: navigation blocked to {Host}{Path}", target.Host, target.AbsolutePath);
        }

        /// <summary>
        /// Loads <paramref name="url"/> even when it is already the <see cref="Source"/>: WPF #867, clicking
        /// the site you are on takes you home and Reload reloads. Assigning an equal Source raises no
        /// change, so it would do nothing. Source is kept in step (the fallback panel names it).
        /// </summary>
        public void Navigate(Uri url)
        {
            _explicitNavigate = true;
            try { Source = url; } finally { _explicitNavigate = false; }
            NavigationRequests++;
            // A sandbox never loads a real site (as ApplySource): the panel may name it, the engine never gets it.
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(url)) { RefusedNavigations++; return; }
            if (_web is null) return;
            try { _web.Navigate(url); }
            catch (Exception ex) { Log.Debug("WebHost: Navigate failed: {Error}", ex.Message); }
        }

        /// <summary>The URL as the fallback panel names it: WebAssetServer's ccp_t token never reaches the screen.</summary>
        internal static string WithoutToken(Uri src)
        {
            if (!src.IsAbsoluteUri || src.Query.Length < 2) return src.ToString();
            var query = string.Join('&', src.Query[1..].Split('&').Where(p => !p.StartsWith("ccp_t=", StringComparison.Ordinal)));
            return new UriBuilder(src) { Query = query }.Uri.ToString();
        }

        /// <summary>Count of <see cref="Navigate"/> calls; a headless test's view of them.</summary>
        internal int NavigationRequests { get; private set; }

        /// <summary>Count of <see cref="Navigate"/> calls the sandbox kept from the engine.</summary>
        internal int RefusedNavigations { get; private set; }

        private bool _explicitNavigate;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SourceProperty) ApplySource();
        }

        private void ApplySource()
        {
            if (_txtSource is null) return; // fired before the XAML loaded
            var src = Source;
            // src! because NativeWebView.Source is annotated non-nullable while its StyledProperty's
            // own default IS null - clearing the page back to null is a real state, not a bug.
            // A sandbox never loads a real site (see OnNavigationStarted); the panel may still name it.
            if (_web is not null)
            {
                if (!_explicitNavigate) _web.Source = ConditioningControlPanel.Services.SandboxNet.Allows(src) ? src! : null!;
                return;
            }
            // No engine: the panel at least names the page that was meant to load.
            _txtSource.Text = src is null ? "" : WithoutToken(src);
            _txtSource.IsVisible = src is not null;
        }

        /// <summary>
        /// Walks every adapter the package knows about and returns null as soon as one is installed
        /// that NativeWebView would actually host on this OS (see <see cref="DecideUnavailableReason"/>).
        /// The Headless adapter never counts, and nothing counts outside the real desktop app, so
        /// <c>--render-all</c>, <c>--smoke</c> and the tests always draw the fallback.
        /// </summary>
        private static string? ProbeUnavailableReason()
        {
            var adapters = new List<DetailedWebViewAdapterInfo>();
            foreach (WebViewAdapterType type in Enum.GetValues(typeof(WebViewAdapterType)))
            {
                if (type == WebViewAdapterType.Unknown) continue;
                try
                {
                    adapters.Add(WebViewAdapterInfo.GetAdapterInfo(type));
                }
                catch (Exception)
                {
                    // The macOS adapter's static constructor throws off macOS. An adapter that
                    // cannot even be asked about is, for our purposes, not there.
                }
            }
            // Only the real desktop app (StartWithClassicDesktopLifetime) hosts an engine. Tests (bare dispatcher or
            // headless session) and --render-all/--smoke (SetupWithoutStarting) have no desktop lifetime, so they
            // always draw the fallback, even on a box with WPE/WebKitGTK installed.
            var headless = Application.Current?.ApplicationLifetime is not global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            return DecideUnavailableReason(adapters, OperatingSystem.IsLinux(), headless, LinuxOptIn);
        }

        /// <summary>
        /// CCP_LINUX_WEBVIEW = <c>wpe</c> | <c>gtk</c>, else null. Linux web views stay OFF by default: in live runs on
        /// KDE Wayland + XWayland both 12.1 engines loaded the page but requestAnimationFrame never kept running
        /// (WPE froze after its first frame, GTK painted nothing), so every rAF-heartbeat host closed itself.
        /// The opt-in exists to re-check that (docs/avalonia-decisions.md, 2026-10-12 webview-probe-fix).
        /// </summary>
        internal static string? LinuxOptIn =>
            Environment.GetEnvironmentVariable("CCP_LINUX_WEBVIEW")?.Trim().ToLowerInvariant() switch { "wpe" => "wpe", "gtk" => "gtk", _ => null };

        internal const string NoEngineReason = "No native web view on this platform.";

        internal const string LinuxOffReason =
            "Web views are off on Linux by default (they stall here). Set CCP_LINUX_WEBVIEW=wpe or gtk to try one.";

        /// <summary>
        /// The availability decision. Without a Linux opt-in: an installed adapter offering NativeControlHost
        /// (WebView2 on Windows, WKWebView on macOS); 12.1 reports none on Linux, so Linux draws the fallback.
        /// With <paramref name="linuxOptIn"/> it mirrors the package's own choice (WebViewAdapter.CreateFactory):
        /// <c>wpe</c> = WPE (picked first) or WebKitGTK installed; <c>gtk</c> = WebKitGTK installed (WPE skipped).
        /// </summary>
        internal static string? DecideUnavailableReason(IEnumerable<DetailedWebViewAdapterInfo> adapters, bool isLinux, bool headless, string? linuxOptIn = null)
        {
            string? firstReason = null;
            var linuxOff = false;
            foreach (var info in adapters)
            {
                if (!info.IsSupported || info.Type is WebViewAdapterType.Unknown or WebViewAdapterType.Headless) continue;
                var hostable = (isLinux, linuxOptIn) switch
                {
                    (true, "wpe") => info.Type is WebViewAdapterType.WpeWebKit or WebViewAdapterType.WebKitGtk,
                    (true, "gtk") => info.Type is WebViewAdapterType.WebKitGtk,
                    _ => info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.NativeControlHost),
                };
                if (info.IsInstalled && hostable && !headless)
                    return null;
                // An engine is installed but off by default: say so and name the opt-in, not "no web view".
                if (isLinux && linuxOptIn is null && !headless && info.IsInstalled
                    && info.Type is WebViewAdapterType.WpeWebKit or WebViewAdapterType.WebKitGtk)
                    linuxOff = true;
                // Prefer the first supported-but-missing engine's message: on Linux that is the
                // actionable "Install webkit2gtk 4.0+ package", not "not supported on this platform".
                if (firstReason is null && !string.IsNullOrWhiteSpace(info.UnavailableReason))
                    firstReason = info.UnavailableReason;
            }
            return linuxOff ? LinuxOffReason : firstReason ?? NoEngineReason;
        }
    }
}
