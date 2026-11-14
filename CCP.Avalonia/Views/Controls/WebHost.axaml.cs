using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;
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
    /// GoBack/GoForward, Stop and Refresh; nothing needs
    /// them yet, so they are not wrapped. Home browser popups are routed in place. Native zoom is applied through WebViewZoom; it has no document-created script
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
        internal bool SuppressNewWindows { get; set; }
        internal event Action? BrowserProcessFailed;
        /// <summary>Per-host native settings, run on the UI thread once for every engine the view builds
        /// (Spiral's WebView2 chrome switches). A throwing hook is logged, never fatal.</summary>
        internal Action<IPlatformHandle?>? ConfigureNative { get; set; }
        private IDisposable? _failureWatch;
        internal void OnBrowserProcessFailed() => BrowserProcessFailed?.Invoke();
        /// <summary>The engine granted a page's fullscreen request (WPF ContainsFullScreenElementChanged).
        /// Native only: a page message can never raise it.</summary>
        internal event Action? NativeFullscreenEntered;
        internal event Action? NativeFullscreenExited;
        private IDisposable? _fullscreenWatch;
        internal void OnNativeFullscreenEntered() => NativeFullscreenEntered?.Invoke();
        /// <summary>Keys the page never sees, taken at the engine's input (Platform/WebKeySignal), any frame.
        /// Runs on the engine's input thread, so it must be pure. Set before the first navigation.</summary>
        internal Func<global::Avalonia.Input.Key, global::Avalonia.Input.KeyModifiers, bool>? NativeKeyFilter { get; set; }
        internal event Action<global::Avalonia.Input.Key, global::Avalonia.Input.KeyModifiers>? NativeKeyDown;
        private IDisposable? _keyWatch;
        internal void OnNativeKeyDown(global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers mods) => NativeKeyDown?.Invoke(key, mods);
        internal void OnNativeFullscreenExited() => NativeFullscreenExited?.Invoke();
        internal IDisposable? PreserveForMove() => _web?.BeginReparenting();

        /// <summary>Per-host WebView2 options, set before attachment. Other adapters ignore these.</summary>
        public string? WindowsBrowserArguments { get; set; }
        public string? WindowsUserDataFolder { get; set; }

        // WPF JustDropHostService.cs:111,132-135. Keep custom arguments off shared profiles.
        internal void ApplyWindowsOptions(object? args)
        {
            if (args is not WindowsWebView2EnvironmentRequestedEventArgs windows) return;
            if (WindowsBrowserArguments is { } arguments) windows.AdditionalBrowserArguments = arguments;
            if (WindowsUserDataFolder is { } folder) windows.UserDataFolder = folder;
        }

        /// <summary>
        /// The URL the page actually sits on after its last completed navigation: redirects,
        /// in-page links and script hops included, unlike <see cref="Source"/>, which is only
        /// what a caller last asked for. Null until a navigation completes. WPF's
        /// <c>BrowserService.GetCurrentUrl</c>.
        /// </summary>
        public Uri? CurrentUrl { get; private set; }

        // The last accepted top-level start, only while it has not completed. NativeWebView's
        // message event has no source, so callers must not treat this as a message's proven origin.
        internal Uri? ActiveNavigation { get; private set; }

        /// <summary>
        /// Raised on the UI thread with <see cref="CurrentUrl"/> after every top-level navigation
        /// completes, failed ones included, as WPF's <c>BrowserService.NavigationCompleted</c>
        /// does (BrowserService.cs:1435-1443) - the live-URL signal MainWindow.Browser.cs:132
        /// hangs its status line and catalogue lookup on.
        /// </summary>
        public event Action<Uri>? NavigationCompleted;
        internal event Action? NavigationStarting;

        /// <summary>Completion outcome, for decorative embeds that must fall back on a failed load.</summary>
        public event Action<Uri, bool>? NavigationFinished;
        internal event Action? NavigationBeginning;

        /// <summary>
        /// The seam the engine's NavigationCompleted routes through, internal so a headless test
        /// (which never has an engine) can drive the same path. A completion with no URL raises
        /// nothing: there is nothing live to report.
        /// </summary>
        internal void OnNavigationCompleted(Uri? url, bool success = true)
        {
            ActiveNavigation = null;
            if (url is null || _panicBlank) return;
            CurrentUrl = url;
            if (InjectCarrier) _ = InvokeScriptAsync(CarrierShim);
            NavigationCompleted?.Invoke(url);
            NavigationFinished?.Invoke(url, success);
        }

        /// <summary>
        /// A string the page posted (<c>window.invokeCSharpAction</c>, which the engine injects; it
        /// JSON-stringifies objects). The stand-in for WPF's <c>CoreWebView2.WebMessageReceived</c>.
        /// </summary>
        public event Action<string>? WebMessage;

        // The native adapter asks at first attachment, before navigation. Keep OS setup in the caller.
        internal event Action<WebViewEnvironmentRequestedEventArgs>? EnvironmentRequested;

        // Same callback for the native adapter and headless environment-policy tests.
        internal void OnEnvironmentRequested(WebViewEnvironmentRequestedEventArgs args) =>
            EnvironmentRequested?.Invoke(args);

        /// <summary>
        /// One extra request header for a url, or null (the usual answer). The stand-in for WPF's
        /// <c>AddWebResourceRequestedFilter</c> + <c>Request.Headers.SetHeader</c> (JustDropHostService.AttachAuthHeader):
        /// the caller's rule decides, per request, whether its credential rides along. Never logged.
        /// </summary>
        public Func<Uri, (string Name, string Value)?>? RequestHeader { get; set; }

        /// <summary>Requests <see cref="RequestHeader"/> actually stamped (the engine accepted the header; never the value).</summary>
        internal int StampedRequests { get; private set; }

        internal void StampRequestHeader(WebResourceRequestedEventArgs e)
        {
            try
            {
                if (e.Request?.Uri is not { } uri) return;
                var headers = e.Request.Headers;
                GateScrolllerRequest(uri, n => headers.TryGetValue(n, out var v) ? v : null, headers.TrySet);
                Stamp(uri, headers.TrySet);
            }
            catch (Exception ex) { Log.Debug("WebHost: request header not set: {Error}", ex.Message); }
        }

        /// <summary>
        /// False for the in-app web browser only: the user's own browsing is not the app fetching
        /// Scrolller (WPF ScrolllerWebGate leaves MainWindow.Browser untouched). Every other page is gated.
        /// </summary>
        public bool GateScrolller { get; set; } = true;

        // The dashboard browser is subject to the app offline setting, including held requests.
        public bool BlockWhenOffline { get; set; }

        /// <summary>Scrolller requests that left with the honest user-agent (a headless test's view).</summary>
        internal int ScrolllerRewrites { get; private set; }

        /// <summary>Navigations and loads refused because the Scrolller licence switch is off.</summary>
        internal int ScrolllerRefusals { get; private set; }

        /// <summary>
        /// WPF ScrolllerWebGate.Handle (main c45d58a65), header half: a Scrolller request goes out with
        /// <see cref="ScrolllerSource.HonestUserAgent"/> and a foreign Referer becomes ours. This engine's
        /// WebResourceRequested carries no response, so it cannot answer locally (403 when off, the device
        /// cache, the daily share): ponytail: sync7-scrolller-d-native adds a WebKitGTK content filter and
        /// the WebView2 filter. Off, top-level loads are refused instead (<see cref="RefusesScrolller"/>).
        /// </summary>
        internal bool GateScrolllerRequest(Uri uri, Func<string, string?> get, Func<string, string, bool> trySet)
        {
            if (!GateScrolller || ScrolllerWebRules.Decide(Absolute(uri), ScrolllerPolicy.Enabled) != ScrolllerWebDecision.Rewrite)
                return false;
            if (!trySet("User-Agent", ScrolllerSource.HonestUserAgent)) return false;
            if (!ScrolllerWebRules.IsOurReferer(get("Referer"))) trySet("Referer", ScrolllerWebRules.OurReferer);
            ScrolllerRewrites++;
            return true;
        }

        private static string? Absolute(Uri? u) => u is { IsAbsoluteUri: true } ? u.AbsoluteUri : null;

        /// <summary>True (and counted) when <paramref name="target"/> is Scrolller while the licence switch is off.</summary>
        private bool RefusesScrolller(Uri? target)
        {
            if (!GateScrolller || ScrolllerWebRules.Decide(Absolute(target), ScrolllerPolicy.Enabled) != ScrolllerWebDecision.Block)
                return false;
            ScrolllerRefusals++;
            Log.Information("WebHost: Scrolller is switched off, {Host} not loaded", target!.Host);
            return true;
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
        /// With <see cref="InjectCarrier"/> WebKitGTK installs it at document start, before the first navigation
        /// of each engine (<see cref="Platform.WebViewCarrier"/>); it also runs after every completed navigation
        /// as the fallback on engines without that seam (WPE) or when the native install fails.
        /// </summary>
        internal const string CarrierShim =
            "(function(){var w=window;if(w.chrome&&w.chrome.webview&&w.chrome.webview.postMessage)return;"
            + "if(typeof w.invokeCSharpAction!=='function')return;var t=new EventTarget();"
            + "t.postMessage=function(m){w.invokeCSharpAction(typeof m==='string'?m:JSON.stringify(m));};"
            + "w.chrome=w.chrome||{};w.chrome.webview=t;"
            + "if(typeof w.__ccpRnPush!=='function')w.__ccpRnPush=function(j){var m;try{m=JSON.parse(j);}catch(e){return;}"
            + "t.dispatchEvent(new MessageEvent('message',{data:m}));};})();";

        /// <summary>Install <see cref="CarrierShim"/> at document start where supported, else after each completed
        /// navigation (see there). Set before attaching the host or navigating.</summary>
        public bool InjectCarrier { get; set; }

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post): the page's string push, else a message
        /// event on <c>chrome.webview</c>. Every hosted page shares this one carrier script.</summary>
        public Task<string?> PostJsonAsync(string json)
        {
            return PostRewrittenAsync(ScrolllerFrame(json, CurrentUrl ?? Source));
        }

        /// <summary>The frame rule, pure. A Windows view that WPF's gate answers gets the raw Scrolller urls (as WPF: cached
        /// copies, whole pictures, clips and ranges passed through and charged by the real bytes). Linux (the engine blocks
        /// scrolller.com) and a view whose gate is missing or not reported yet get this head's loopback relay.</summary>
        internal static bool FramesUseRelay(bool? gate, bool windows) => !(windows && gate == true);

        /// <summary>One host -&gt; page frame with its Scrolller urls as <see cref="FramesUseRelay"/> says (Core ScrolllerRelay
        /// for the relay; a remote site never gets a relay url).</summary>
        internal string ScrolllerFrame(string json, Uri? page)
            => FramesUseRelay(_scrolllerGate, OperatingSystem.IsWindows())
               && ConditioningControlPanel.Services.Fyp.Online.ScrolllerRelay.LoopbackOrigin(page) is { } origin
                ? ConditioningControlPanel.Services.Fyp.Online.ScrolllerRelay.RewriteJson(json, origin)
                : json;

        /// <summary><see cref="PostJsonAsync"/> after the Scrolller rewrite (every host's frames, the Core relay rule).</summary>
        private Task<string?> PostRewrittenAsync(string json) => InvokeScriptAsync(
            "(function(m){try{if(typeof window.__ccpRnPush==='function'){window.__ccpRnPush(JSON.stringify(m));return;}"
            + "var w=window.chrome&&window.chrome.webview;if(w&&w.dispatchEvent){w.dispatchEvent(new MessageEvent('message',{data:m}));}}catch(e){}})("
            + json + ");");

        /// <summary>WPF ChaosWebViewHost.cs:1686-1697, 1721-1732: consume page logs before
        /// dispatching game messages. Missing and unknown levels are debug; failures stay visible.</summary>
        internal static bool HandlePageLog(Newtonsoft.Json.Linq.JObject message, string tag)
        {
            if ((string?)message["type"] != "log") return false;
            var level = ((string?)message["level"])?.Trim().ToLowerInvariant() switch
            {
                "error" or "fatal" => Serilog.Events.LogEventLevel.Error,
                "warn" or "warning" => Serilog.Events.LogEventLevel.Warning,
                "info" or "information" => Serilog.Events.LogEventLevel.Information,
                _ => Serilog.Events.LogEventLevel.Debug,
            };
            Log.Write(level, "{Tag}[page]: {Msg}", tag, (string?)message["msg"]);
            return true;
        }

        /// <summary>The seam the engine's WebMessageReceived routes through (headless tests drive it).</summary>
        internal void OnWebMessage(string? body)
        {
            // WPF ChaosWebViewHost.cs:1700-1737 contains the whole dispatch, including field casts.
            try
            {
                if (!string.IsNullOrEmpty(body)) WebMessage?.Invoke(body);
            }
            catch (Exception ex) { Log.Debug("WebHost.OnWebMessage: {E}", ex.Message); }
        }

        /// <summary>Return keyboard focus to the native page after a title-bar Escape.</summary>
        public void FocusPage()
        {
            if (_web != null) _web.Focus();
            else Focus();
        }

        /// <summary>
        /// True when THIS instance built an adapter. <see cref="IsAvailable"/> is the process-wide
        /// probe; the constructor can still fail after it passes, and a caller about to drive the
        /// page through script needs to know about this control, not about the machine.
        /// </summary>
        // WPF MainWindow.Browser.cs:2740-2818 moves the existing page without reloading.
        internal IDisposable? BeginReparenting() => _web?.BeginReparenting();

        public bool HasEngine => _web is not null || ScriptInvoker != null;

        // WPF MainWindow.Browser.cs:249-280: localized initialization failure categories.
        internal string FailureStatusKey { get; private set; } = "label_not_installed";
        internal static string FailureKey(Exception error) => error switch
        {
            DllNotFoundException => "label_missing_dll",
            System.Runtime.InteropServices.COMException => "label_com_error",
            InvalidOperationException => "label_not_installed",
            _ => "label_error_2",
        };

        // Native script boundary; tests supply a per-instance page adapter without opening a desktop.
        internal Func<string, Task<string?>>? ScriptInvoker { get; set; }
        internal Func<bool?>? AudioProbe { get; set; }
        internal bool? IsPlayingAudio() => AudioProbe != null ? AudioProbe() : _web == null ? null : Platform.WebAudioProbe.IsPlaying(_web.TryGetPlatformHandle());

        /// <summary>Live pause from the Workshop switch (WebHostMedia.SetPausedAsync). Re-applied mutes honour it, so a
        /// navigation while paused stays muted, as WPF's engine-level IsMuted does.</summary>
        internal bool MediaPaused { get; set; }

        // WPF BrowserService.cs:119-122, 245-246: engine-level mute (cross-origin frames included). The preference is
        // remembered before an adapter exists and reapplied to every rebuilt engine before its first navigation.
        private bool? _engineMuted;
        internal Func<bool, Task<bool>>? NativeMuteInvoker { get; set; }
        internal Task<bool> SetEngineMutedAsync(bool muted)
        {
            _engineMuted = muted;
            if (NativeMuteInvoker != null) return NativeMuteInvoker(muted);
            try { return Platform.WebViewAudio.SetMutedAsync(_web?.TryGetPlatformHandle(), muted); }
            catch (Exception ex) { Log.Debug("WebHost: native mute failed: {E}", ex.Message); return Task.FromResult(false); }
        }

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
            if (string.IsNullOrEmpty(javaScript)) return null;
            try
            {
                if (ScriptInvoker != null) return await ScriptInvoker(javaScript);
                return _web == null ? null : await _web.InvokeScript(javaScript);
            }
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
                ConditioningControlPanel.Services.LinuxDependencies.WebViews));

        private readonly Panel _webSlot;
        private readonly Border _fallback;
        private readonly TextBlock _txtReason, _txtSource;
        private NativeWebView? _web;

        // Per-instance factory seam for headless recovery tests.
        internal Func<NativeWebView>? NativeViewFactory { get; set; }
        internal NativeWebView? NativeView => _web;
        internal Func<NativeWebView, Action, IDisposable>? FailureWatchFactory { get; set; }

        public WebHost()
        {
            TrackScrolllerHost();
            AvaloniaXamlLoader.Load(this);
            _webSlot = this.FindControl<Panel>("WebSlot")!;
            _fallback = this.FindControl<Border>("Fallback")!;
            _txtReason = this.FindControl<TextBlock>("TxtReason")!;
            _txtSource = this.FindControl<TextBlock>("TxtSource")!;

            if (IsAvailable)
            {
                CreateNativeView();
            }
            else
            {
                // Linux: name the distro's install command (docs/avalonia-linux-install.html) instead of
                // the adapter's generic "Install webkit2gtk 4.0+ package.", which stays the fallback.
                // CCP_LINUX_WEBVIEW=off: the reason is the user's own choice, not a missing package.
                var hint = LinuxChoice == "off" ? null : _linuxHint.Value;
                _txtReason.Text = hint?.Text ?? UnavailableReason!;   // the hint ends with LinuxDependencies.RestartHint
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

        // Only the Home browser opts in. Other embeds keep their existing popup policy.
        public bool RouteBrowserPopups { get; set; }

        // Home retries a synchronous adapter construction failure on the next navigation (Navigate).
        public bool RetryFailedConstruction { get; set; }

        // Native URL boundary, like ScriptInvoker. No cached URL fallback: unknown pages get no exemption.
        internal Func<string?>? DocumentUrlReader { get; set; }

        // WPF BrowserService.cs:538-562: handled even when blocked, never a second OS window.
        internal bool OnBrowserPopup(Uri? target)
        {
            if (!RouteBrowserPopups) return false;
            if (target is not { IsAbsoluteUri: true }) return true;
            if (ConditioningControlPanel.Services.Browser.BrowserPopupRule.ShouldBlock(
                    (DocumentUrlReader ?? (() => Platform.ScrolllerWebHostGate.CurrentDocumentUrl(_web?.TryGetPlatformHandle())))(), target.AbsoluteUri)) return true;
            Navigate(target);
            return true;
        }

        private void ClearNativeWatches()
        {
            try { _failureWatch?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Browser failure cleanup failed"); } finally { _failureWatch = null; }
            try { _fullscreenWatch?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Fullscreen signal cleanup failed"); } finally { _fullscreenWatch = null; }
            try { _keyWatch?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Key signal cleanup failed"); } finally { _keyWatch = null; }
        }

        private void ApplyNativeSettings(NativeWebView native, IPlatformHandle? handle)
        {
            if (!ReferenceEquals(_web, native) || ConfigureNative is not { } configure) return;
            try { configure(handle ?? native.TryGetPlatformHandle()); }
            catch (Exception ex) { Log.Debug(ex, "Native browser settings failed"); }
        }

        private void AttachFailureWatch(NativeWebView native, bool reportFailure)
        {
            if (!ReferenceEquals(_web, native) || BrowserProcessFailed == null || _failureWatch != null) return;
            try
            {
                void Failed() { if (ReferenceEquals(_web, native)) OnBrowserProcessFailed(); }
                _failureWatch = FailureWatchFactory?.Invoke(native, Failed)
                    ?? Platform.TrailerBrowserPolicy.Attach(native.TryGetPlatformHandle(), Failed);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Browser failure subscription unavailable");
                if (reportFailure) OnBrowserProcessFailed();
            }
        }

        // The host stays put (including when popped out), but the failed native child must not be reused.
        internal bool RecreateNativeView()
        {
            try
            {
                BrowserZoomAdapterDestroyed();
                ClearNativeWatches();
                if (_web is { } old) _webSlot.Children.Remove(old);
                _web = null;
                NewScrolllerEngine(); // invalidate delayed reports from the old engine
                CurrentUrl = null;
                return CreateNativeView();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser replacement failed");
                return false;
            }
        }

        private bool CreateNativeView()
        {
            if (!IsAvailable && NativeViewFactory is null) return false;
            NativeWebView? native = null;
            try
            {
                native = NativeViewFactory?.Invoke() ?? new NativeWebView();
                _web = native;
                // A native child does not inherit the parent's brush (Spiral's no-white-flash sink).
                BindNativeBackground(native);
                // WebKitGTK unless WPE was chosen: the package picks WPE first, and on wpewebkit 2.54+ its adapter
                // throws EntryPointNotFoundException in an async void, which takes the whole app down.
                var preferGtk = LinuxChoice != "wpe";
                _web.EnvironmentRequested += (_, e) =>
                {
                    if (preferGtk && e is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe) wpe.PreferWebKitGtkInstead = true;
                    // Windows: the WebView2 profile under UserData, never beside the exe (Platform/WebViewProfile).
                    Platform.WebViewProfile.Apply(e);
                    ApplyWindowsOptions(e);
                };
                // Subscribed once, here, rather than when a caller sets AllowNavigation: the
                // gate has to be live for the FIRST navigation too, and a caller that assigns
                // the predicate and the Source in that order would otherwise race the engine.
                native.EnvironmentRequested += (_, e) => OnEnvironmentRequested(e);
                native.AdapterDestroyed += (_, _) => { if (ReferenceEquals(_web, native)) { BrowserZoomAdapterDestroyed(); ClearNativeWatches(); } };
                native.AdapterCreated += (_, e) =>
                {
                    if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { ApplyNativeSettings(native, e.TryGetPlatformHandle()); AttachFailureWatch(native, false); ZoomAdapterCreated(native, () => e.TryGetPlatformHandle()); }
                    else global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { ApplyNativeSettings(native, null); AttachFailureWatch(native, false); if (ReferenceEquals(_web, native)) TryBrowserZoomAdapterReady(); });
                };
                native.NavigationStarted += (_, e) => { if (ReferenceEquals(_web, native)) OnNavigationStarted(native, e); else e.Cancel = true; };
                native.NavigationCompleted += (_, e) => {
                    if (!ReferenceEquals(_web, native)) return;
                    // The native core exists now; subscribe before observers can reveal a scene.
                    AttachFailureWatch(native, true);
                    if (BrowserProcessFailed != null && _failureWatch == null) return;
                    if ((NativeFullscreenEntered != null || NativeFullscreenExited != null) && _fullscreenWatch == null)
                        try { _fullscreenWatch = Platform.WebFullscreenSignal.Attach(native.TryGetPlatformHandle(), OnNativeFullscreenEntered, OnNativeFullscreenExited); }
                        catch (Exception ex) { Log.Debug(ex, "Fullscreen signal subscription failed"); }
                    if (NativeKeyFilter is { } wants && _keyWatch == null)
                        try { _keyWatch = Platform.WebKeySignal.Attach(native.TryGetPlatformHandle(), wants, OnNativeKeyDown); }
                        catch (Exception ex) { Log.Debug(ex, "Key signal subscription failed"); }
                    OnNavigationCompleted(e.Request ?? native?.Source, e.IsSuccess);
                };
                native.NewWindowRequested += (_, e) => { if (SuppressNewWindows) e.Handled = true; else e.Handled = OnBrowserPopup(e.Request); };
                native.WebMessageReceived += (_, e) => OnWebMessage(e.Body);
                native.WebResourceRequested += (_, e) => StampRequestHeader(e);
                // The Scrolller licence on pages that load Scrolller themselves (WPF ScrolllerWebGate on every core):
                // the WebView2 gate on Windows, a WebKitGTK block filter on Linux. The first navigation waits for it.
                Platform.ScrolllerWebHostGate.Attach(native, () => ReferenceEquals(_web, native) ? NewScrolllerEngine() : _ => { });
                _webSlot.Children.Add(native);
                _fallback.IsVisible = false;
                return true;
            }
            catch (Exception ex)
            {
                if (native != null) _webSlot.Children.Remove(native);
                _web = null;
                FailureStatusKey = FailureKey(ex);
                _txtReason.Text = ex.Message;
                _fallback.IsVisible = true;
                Log.Warning(ex, "Browser creation failed");
                return false;
            }
        }

        internal void BindNativeBackground(NativeWebView native) =>
            native.Bind(NativeWebView.BackgroundProperty, this.GetObservable(BackgroundProperty));

        internal void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            NavigationStarting?.Invoke();
            var target = e.Request;
            ActiveNavigation = null;
            if (e.Cancel) return;
            // Panic teardown must pass a caller's page allowlist, including on reattachment.
            // Late redirects from the stopped document must not replace the blank page.
            if (_panicBlank) { e.Cancel = target != PanicBlank; return; }
            TryBrowserZoomAdapterReady();
            _scrolllerPanicTarget = target;
            if (RefusesScrolller(target)) { e.Cancel = true; return; }
            // A CCP_USERDATA_DIR sandbox never loads a real site, whatever the caller's gate says.
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(target)) { e.Cancel = true; return; }
            // No Scrolller gate on this engine (yet, or at all): nothing but this head's own pages loads.
            if (!ScrolllerGateAllows(_scrolllerGate, target))
            {
                e.Cancel = true;
                Log.Warning("WebHost: navigation held, no Scrolller gate on this web view ({Host})", target?.Host);
                return;
            }
            if (target?.Scheme != "about") _loadedOnThisEngine = true;
            // The Scrolller licence switch is off: no web view of this app opens a Scrolller page (WPF ScrolllerWebGate Block).
            if (ConditioningControlPanel.Services.Fyp.Online.ScrolllerPolicyService.Blocks(target?.AbsoluteUri)) { e.Cancel = true; return; }
            var gate = AllowNavigation;
            if (gate is null) { RecordAllowedNavigation(target); NavigationBeginning?.Invoke(); return; }
            // No URL to judge: refuse. A navigation the gate cannot see is exactly the one a
            // hostile page would use to slip past it.
            if (target is null) { e.Cancel = true; return; }
            if (gate(target)) { RecordAllowedNavigation(target); NavigationBeginning?.Invoke(); return; }
            e.Cancel = true;
            // Host + path only: a signed media URL's query string must not reach the log.
            Log.Warning("WebHost: navigation blocked to {Host}{Path}", target.Host, target.AbsolutePath);
        }

        internal void RecordAllowedNavigation(Uri? target) => ActiveNavigation = target;

        /// <summary>
        /// Loads <paramref name="url"/> even when it is already the <see cref="Source"/>: WPF #867, clicking
        /// the site you are on takes you home and Reload reloads. Assigning an equal Source raises no
        /// change, so it would do nothing. Source is kept in step (the fallback panel names it).
        /// </summary>
        public bool Navigate(Uri url)
        {
            ActiveNavigation = null;
            _panicBlank = false;
            CancelPendingNavigation();
            NavigationRequests++;
            // Do not retain a request the reported gate refuses: a later gate report must not load it after fallback.
            if (_scrolllerGate is not null && !ScrolllerGateAllows(_scrolllerGate, url)) return false;
            var previous = Source;
            _explicitNavigate = true;
            try { Source = url; } finally { _explicitNavigate = false; }
            // Refused or engine-less requests stay named on the fallback panel; the caller learns nothing loaded.
            if (RefusesScrolller(url)) { RefusedNavigations++; return false; }
            // A sandbox never loads a real site (as ApplySource): the panel may name it, the engine never gets it.
            if (!ConditioningControlPanel.Services.SandboxNet.Allows(url)) { RefusedNavigations++; return false; }
            // WPF MainWindow.Browser.cs:48, :297: a failed adapter construction is retried on the next navigation.
            if (RetryFailedConstruction && _web is null && NavigationInvoker is null && (IsAvailable || NativeViewFactory != null))
                CreateNativeView();
            if (_web is null && NavigationInvoker is null) return false;
            if (_scrolllerGate is null) return true; // held until the gate reports
            try
            {
                if (NavigationInvoker is { } navigate)
                {
                    if (!navigate(url)) { RestoreSource(previous); return false; }
                }
                else _web!.Navigate(url);
                return true;
            }
            catch (Exception ex)
            {
                RestoreSource(previous);
                Log.Debug("WebHost: Navigate failed: {Error}", ex.Message);
                return false;
            }
        }

        private void RestoreSource(Uri? previous)
        {
            _explicitNavigate = true;
            try { Source = previous; } finally { _explicitNavigate = false; }
        }

        // Null means a newer navigation superseded this request, so the caller must not fall back.
        internal async Task<bool?> NavigateWhenReadyAsync(Uri url)
        {
            CancelPendingNavigation();
            if (_web is null && NavigationInvoker is null) return false;
            while (_scrolllerGate is null)
            {
                var pending = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingNavigation = pending;
                var completed = await Task.WhenAny(pending.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                if (_pendingNavigation != pending) return null;
                _pendingNavigation = null;
                if (completed != pending.Task) return false;
                var ready = await pending.Task;
                if (ready is null) return null;
            }
            return Navigate(url);
        }

        private TaskCompletionSource<bool?>? _pendingNavigation;
        internal void CancelPendingNavigation()
        {
            var pending = _pendingNavigation;
            _pendingNavigation = null;
            pending?.TrySetResult(null);
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

        // Headless tests supply an engine without loading a native web view.
        internal Func<Uri, bool>? NavigationInvoker { get; set; }

        // A caller sets this before the native adapter is attached. WebView2 installs it on each
        // recreated core before the held first navigation; other engines retain the load fallback.
        internal string? WindowsDocumentStartScript { get; set; }
        internal Func<string, Task>? DocumentStartInstaller { get; set; }

        internal async Task PrepareScrolllerGate(int engine, bool gated)
        {
            if (engine != _scrolllerEngine) return;
            var report = ++_gateReport;
            bool Stale() => engine != _scrolllerEngine || report != _gateReport;
            // WPF BrowserService.cs:245-246: a fresh engine gets the saved mute before navigation.
            if (_engineMuted is { } muted)
            {
                await BoundedNativeStep(SetEngineMutedAsync(muted), "native mute");
                if (Stale()) return;
            }
            // WebKitGTK: the opt-in carrier before the page's own scripts, once per engine.
            if (InjectCarrier)
            {
                _carrierInstall ??= InstallCarrierAsync();
                await BoundedNativeStep(_carrierInstall, "carrier install");
                if (Stale()) return;
            }
            if (WindowsDocumentStartScript is { } script && (OperatingSystem.IsWindows() || DocumentStartInstaller != null))
            {
                try
                {
                    if (DocumentStartInstaller is { } install) await install(script).WaitAsync(TimeSpan.FromSeconds(5));
                    else if (OperatingSystem.IsWindows()) await InstallWindowsDocumentStart(script).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex) { Log.Warning("WebHost: document-start script failed: {Error}", ex.Message); }
            }
            OnScrolllerGate(engine, gated);
        }

        private int _gateReport;
        private Task<bool>? _carrierInstall;
        internal Func<string, Task<bool>>? NativeCarrierInvoker { get; set; }

        // A GLib idle callback that never runs must not hold the first navigation forever.
        private static async Task BoundedNativeStep(Task<bool> step, string what)
        {
            try { await step.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { Log.Debug("WebHost: {What} failed: {E}", what, ex.Message); }
        }

        private async Task<bool> InstallCarrierAsync()
        {
            try
            {
                return await (NativeCarrierInvoker?.Invoke(CarrierShim)
                    ?? Platform.WebViewCarrier.InstallAsync(_web?.TryGetPlatformHandle(), CarrierShim));
            }
            catch (Exception ex) { Log.Debug("WebHost: carrier install failed: {E}", ex.Message); return false; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task InstallWindowsDocumentStart(string script)
        {
            if (_web?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle win || win.CoreWebView2 == IntPtr.Zero)
                throw new InvalidOperationException("WebView2 core unavailable");
            var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(win.CoreWebView2);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }

        private bool _explicitNavigate;

        /// <summary>The Scrolller gate on this engine: null = not reported yet (every navigation held), true = gated,
        /// false = it could not be had (only this head's own pages load).</summary>
        private bool? _scrolllerGate;
        internal bool? ScrolllerGate => _scrolllerGate;

        /// <summary>Which engine the gate's reports belong to, and whether a page started loading on it.</summary>
        private int _scrolllerEngine;
        private bool _loadedOnThisEngine;

        /// <summary>A new engine behind this view, or the old one gone (UI thread). Avalonia.Controls.WebView destroys
        /// the native control on every detach outside BeginReparenting scopes, so a view that is unloaded and
        /// loaded again (a tab switch, a moved view) gets a NEW engine: it is held again until its own gate reports,
        /// and a late report from an older engine is dropped. Returns that engine's report.</summary>
        internal Action<bool> NewScrolllerEngine()
        {
            _scrolllerGate = null;
            ActiveNavigation = null;
            _loadedOnThisEngine = false;
            _carrierInstall = null;
            _gateReport = 0;
            if (_panicBlank) NavigatePanicBlank();
            var engine = ++_scrolllerEngine;
            return gated => _ = PrepareScrolllerGate(engine, gated);
        }

        /// <summary>One engine's gate report (UI thread): the held page loads now. A rebuilt engine already carries
        /// the same Source (assigning it again does nothing), so its held page is loaded with Navigate.</summary>
        internal void OnScrolllerGate(int engine, bool gated)
        {
            if (engine != _scrolllerEngine) return;
            TryBrowserZoomAdapterReady();
            if (BlockWhenOffline && CoreSettings.Current.OfflineMode && Source is { IsAbsoluteUri: true, Scheme: not "about" })
            {
                CancelHeldNavigation();
                _scrolllerGate = gated;
                return;
            }
            _scrolllerGate = gated;
            _pendingNavigation?.TrySetResult(gated);
            var src = Source;
            if (ReloadAfterGate(_web?.Source, src, _loadedOnThisEngine) && ConditioningControlPanel.Services.SandboxNet.Allows(src))
            {
                try { _web!.Navigate(src!); }
                catch (Exception ex) { Log.Debug("WebHost: Navigate failed: {Error}", ex.Message); }
                return;
            }
            ApplySource();
        }

        /// <summary>Pure: the engine already names the page (a rebuilt engine) and nothing loaded on it yet (held or
        /// refused while the gate had not reported) = load it again with Navigate.</summary>
        internal static bool ReloadAfterGate(Uri? engineSource, Uri? source, bool loadedOnThisEngine)
            => source != null && !loadedOnThisEngine && engineSource != null && engineSource == source;

        /// <summary>The navigation rule while the gate is missing, pure: a gated engine loads anything; an ungated one only
        /// this head's own loopback pages (they get Scrolller through the relay only) and about: pages.</summary>
        internal static bool ScrolllerGateAllows(bool? gate, Uri? target)
            => gate == true || (target is { IsAbsoluteUri: true } t
                && (t.Scheme == "about" || (t.Scheme == Uri.UriSchemeHttp && t.Host == "127.0.0.1")));

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SourceProperty)
            {
                ActiveNavigation = null;
                if (Source != null) _panicBlank = false; // a new explicit request may load normally
                if (!_explicitNavigate) CancelPendingNavigation();
                ApplySource();
            }
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
                if (_scrolllerGate is null) return;   // held until the Scrolller gate reports (OnScrolllerGate)
                if (!_explicitNavigate)
                    _web.Source = ConditioningControlPanel.Services.SandboxNet.Allows(src) && !RefusesScrolller(src) ? src! : null!;
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
            var linux = OperatingSystem.IsLinux();
            var sink = AudioSinkReady(linux, headless, () => ConditioningControlPanel.Services.LinuxDependencies.GstPluginPresent("libgstautodetect.so"),
                Environment.GetEnvironmentVariable("CCP_LINUX_WEBVIEW"));
            return DecideUnavailableReason(adapters, linux, headless, LinuxChoice, audioSink: sink);
        }

        /// <summary>The audioSink input of <see cref="DecideUnavailableReason"/>: probed only on the real Linux desktop app,
        /// and an explicit CCP_LINUX_WEBVIEW=gtk overrides a negative probe with one warning.</summary>
        internal static bool AudioSinkReady(bool linux, bool headless, Func<bool> probe, string? env)
        {
            if (!linux || headless || probe()) return true;
            if (!SkipsAudioSinkCheck(env)) return false;
            Log.Warning("WebHost: GStreamer autoaudiosink not found, CCP_LINUX_WEBVIEW=gtk overrides the check (a page's first sound may freeze it)");
            return true;
        }

        /// <summary>An explicit CCP_LINUX_WEBVIEW=gtk overrides the audio-sink probe, so a false negative (a plugin dir the probe
        /// does not know) cannot block every web page for good. The unset default stays gated.</summary>
        internal static bool SkipsAudioSinkCheck(string? env) => env?.Trim().ToLowerInvariant() == "gtk";

        /// <summary>The Linux engine for this process: <see cref="LinuxEngine"/> over CCP_LINUX_WEBVIEW.</summary>
        internal static string LinuxChoice => _linuxChoice.Value;

        private static readonly Lazy<string> _linuxChoice = new(() =>
        {
            var env = Environment.GetEnvironmentVariable("CCP_LINUX_WEBVIEW");
            var choice = LinuxEngine(env, WpeHasLegacyApi);
            if (env?.Trim().ToLowerInvariant() == "wpe" && choice != "wpe")
                Log.Warning("WebHost: CCP_LINUX_WEBVIEW=wpe ignored, libWPEWebKit lacks webkit_web_view_backend_new (wpewebkit 2.54+); using WebKitGTK");
            return choice;
        });

        /// <summary>
        /// CCP_LINUX_WEBVIEW = <c>off</c> -> "off" (fallback panel), <c>wpe</c> -> "wpe" when <paramref name="wpeLegacyApi"/>
        /// says libWPEWebKit still exports the libwpe API that 12.1's WPE adapter calls, anything else -> "gtk" (the default:
        /// it draws continuously in the live runs, docs/avalonia-decisions.html 2026-10-13). The earlier "rAF stalls" on both
        /// engines were WebKitWebProcess aborting without GStreamer's autoaudiosink, not the engines.
        /// </summary>
        internal static string LinuxEngine(string? env, Func<bool> wpeLegacyApi) => env?.Trim().ToLowerInvariant() switch
        {
            "off" => "off",
            "wpe" when wpeLegacyApi() => "wpe",
            _ => "gtk",
        };

        // wpewebkit 2.54 dropped webkit_web_view_backend_new (the libwpe/fdo API); WpeWebViewAdapter.Initialize calls it.
        private static bool WpeHasLegacyApi() =>
            System.Runtime.InteropServices.NativeLibrary.TryLoad("libWPEWebKit-2.0.so.1", out var lib)
            && System.Runtime.InteropServices.NativeLibrary.TryGetExport(lib, "webkit_web_view_backend_new", out _);

        internal const string LinuxOffReason = "Web views are turned off (CCP_LINUX_WEBVIEW=off).";

        internal const string NoAudioSinkReason = "Web pages need the GStreamer audio sink (autoaudiosink, gst-plugins-good).";

        internal const string NoEngineReason = "No native web view on this platform.";

        /// <summary>
        /// The availability decision. Off Linux: an installed adapter offering NativeControlHost (WebView2 on Windows,
        /// WKWebView on macOS). On Linux it mirrors the package's own choice (WebViewAdapter.CreateFactory) for
        /// <paramref name="linuxEngine"/>: <c>gtk</c> = WebKitGTK installed (WPE skipped); <c>wpe</c> = WPE (picked first)
        /// or WebKitGTK installed; <c>off</c> = never. A hostable Linux engine still needs GStreamer's autoaudiosink
        /// (<paramref name="audioSink"/>): without it the first sound aborts the web process and the page freezes.
        /// </summary>
        internal static string? DecideUnavailableReason(IEnumerable<DetailedWebViewAdapterInfo> adapters, bool isLinux, bool headless,
            string linuxEngine = "gtk", bool audioSink = true)
        {
            if (isLinux && linuxEngine == "off") return LinuxOffReason;
            string? firstReason = null;
            foreach (var info in adapters)
            {
                if (!info.IsSupported || info.Type is WebViewAdapterType.Unknown or WebViewAdapterType.Headless) continue;
                var hostable = (isLinux, linuxEngine) switch
                {
                    (true, "wpe") => info.Type is WebViewAdapterType.WpeWebKit or WebViewAdapterType.WebKitGtk,
                    (true, "gtk") => info.Type is WebViewAdapterType.WebKitGtk,
                    _ => info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.NativeControlHost),
                };
                if (info.IsInstalled && hostable && !headless)
                    return isLinux && !audioSink ? NoAudioSinkReason : null;
                // Prefer the first supported-but-missing engine's message: on Linux that is the
                // actionable "Install webkit2gtk 4.0+ package", not "not supported on this platform".
                if (firstReason is null && !string.IsNullOrWhiteSpace(info.UnavailableReason))
                    firstReason = info.UnavailableReason;
            }
            return firstReason ?? NoEngineReason;
        }
    }
}
