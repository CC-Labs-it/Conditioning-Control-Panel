using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// Host for Terms &amp; Conditioning (<c>Resources/web/fuzz/index.html</c>, wire <c>fuzz/CONTRACT.md</c> +
/// Contract v2 in <c>handoff-1122/TC-EDITIONS-PLAN.md</c>). A sibling of the Glassroad host: one windowed
/// <see cref="ChaosWebViewHost"/> (it attaches ScrolllerWebGate), own profile. One tile for everyone:
/// a subscriber gets the full game, a free account the small one (<see cref="TcRules"/>), so the tile
/// is never locked. Every page frame passes <see cref="TcFrameGate"/> first. Hard exits (panic, the
/// title-bar X, a dead page, the watchdog) go through <see cref="Close"/>; the page's own exit ends on
/// its <c>exit-done</c> or at 800 ms. Scrolller pictures and the Sparkles medal live in
/// <c>TcHostService.Media.cs</c>; toy pulses and live settings in <c>TcHostService.Feel.cs</c>; save carry in <c>TcHostService.Save.cs</c>; sticker Sparkles in <c>TcHostService.Stickers.cs</c>; the Llama Expo download in
/// <c>TcHostService.Expo.cs</c>.
/// </summary>
internal static partial class TcHostService
{
    /// <summary>Launcher id, panic registry id and shortcut icon name.</summary>
    public const string GameId = "tc";

    public const string ProductName = "Terms & Conditioning";

    /// <summary>The page's kept-Escape frame (fuzz/host-escape.js HOST_ESCAPE).</summary>
    internal const string KeptEscapeType = "kept-escape";

    private const string StartUrl = "https://ccp.game/fuzz/index.html";

    /// <summary>The web root served as ccp.game. DEBUG only: <c>CCP_TC_WEB_ROOT</c> points it at a
    /// checkout's <c>Resources/web</c> (the game tree is not on main yet). Never read in Release.</summary>
    private static string WebRoot()
    {
#if DEBUG
        var dev = Environment.GetEnvironmentVariable("CCP_TC_WEB_ROOT");
        if (!string.IsNullOrWhiteSpace(dev) && Directory.Exists(dev)) return dev;
#endif
        return Path.Combine(AppContext.BaseDirectory, "Resources", "web");
    }

    /// <summary>The tile shows only when the game's page is on disk.</summary>
    public static bool TreePresent
    {
        get
        {
            try { return File.Exists(Path.Combine(WebRoot(), "fuzz", "index.html")); }
            catch (Exception ex) { Diag.Swallowed(ex); return false; }
        }
    }

    private static ChaosWebViewHost? _host;
    private static DispatcherTimer? _watch, _closeClock;
    private static DateTime _lastAnyUtc, _lastBeatUtc;
    private static DateTime? _closeAskedUtc;
    private static bool _disposing, _focusHeld;
    private static string _edition = TcRules.EditionSmall;

    public static bool IsActive => _host != null;

    /// <summary>The window is in front: the panel lets the page keep a first Escape as its pause.</summary>
    public static bool IsInFront => _host?.IsForeground == true;

    public static bool IsReady => _host?.IsReady == true;

    /// <summary>The edition this window was opened with (read once at launch).</summary>
    internal static string Edition => _edition;

    /// <summary>Hand the page an Escape the panel kept as its pause (pause card, never leave).</summary>
    public static void PostKeptEscape()
    {
        try { _host?.Post(new { type = KeptEscapeType }); _host?.FocusWeb(); }
        catch (Exception ex) { App.Logger?.Debug("Tc: kept Escape post failed: {E}", ex.Message); }
    }

    /// <summary>The subscriber read: the app's Basic bar (whitelist, SubscribeStar, grace) with the
    /// invite week rule in <see cref="TcRules.InviteWeekIsSubscriber"/>. Fails to small.</summary>
    private static string ReadEdition()
    {
        try
        {
            var p = App.Patreon;
            return TcRules.EditionFor(TcRules.IsSubscriber(p?.HasPremiumAccess == true, p?.IsInviteWeekOnly == true));
        }
        catch (Exception ex) { Diag.Swallowed(ex); return TcRules.EditionSmall; }
    }

    /// <summary>Open the game. Idempotent: a live window is only re-focused.</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        if (!TreePresent)
        {
            App.Logger?.Warning("TcHostService: no game tree on disk, nothing to open");
            return;
        }
        try
        {
            _edition = ReadEdition();
            ResetStickers();
            var expoFolder = ExpoOnLaunch();
            _closeAskedUtc = null;
            _focusHeld = false;
            _lastAnyUtc = _lastBeatUtc = DateTime.UtcNow;
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = StartUrl,
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page and the trees it imports (vendor, arcademy, backroom, glassroad/host).
                    ("ccp.game", WebRoot(), CoreWebView2HostResourceAccessKind.Deny),
                    // Local media copies, as every host.
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                },
                // The ready Llama Expo, read-only like ccp.assets (this window only; a pack that lands
                // while the window is open is mapped on this core by MapExpo).
                OnCoreCreated = core => { _core = core; if (expoFolder != null) MapExpo(expoFolder, _expoServing ?? ""); },
                // Own profile (the page's save is its localStorage); the argument string is a constant.
                UserDataFolderName = "browser_data_tc",
                InputEnabled = true,
                IsGame = true,
                StartFullscreen = false,
                OwnedByMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "Tc",
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = kind => RunOnUi(() =>
                {
                    App.Logger?.Warning("Tc: browser process failed ({Kind}) - closing", kind);
                    Close();
                }),
            });
            _host.Show();
            if (_host.Window != null)
            {
                _host.Window.Closed += (_, _) => Close();
                _host.Window.Activated += OnWindowActivated;
            }
            StartWatch();
            WatchFeel(true);
            _host.FocusWeb();
            App.Logger?.Information("TcHostService: launched ({Edition})", _edition);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "TcHostService.Launch failed");
            Close();
        }
    }

    /// <summary>The hard funnel for every exit (panic, title-bar X, dead page, watchdog, shutdown).
    /// Idempotent and immediate: panic never waits on the page.</summary>
    public static void Close()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            try { _watch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _closeClock?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _watch = _closeClock = null;
            StopPulse();      // at once, before anything else can fail
            StopExpo();       // the partial stays for the next press
            WatchFeel(false);
            _core = null;
            _expoServing = null;
            _closeAskedUtc = null;
            var h = _host;
            _host = null;
            if (h != null)
            {
                // Panic wins: the page stops audio and fx on this frame if it gets it before the window goes.
                try { h.Post(TcHostRules.Suspend(true, TcHostRules.ReasonPanic)); } catch (Exception ex) { Diag.Swallowed(ex); }
                try { if (h.Window != null) h.Window.Activated -= OnWindowActivated; } catch (Exception ex) { Diag.Swallowed(ex); }
                DisposePictures();
                try { h.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
                App.Logger?.Information("TcHostService: closed");
            }
        }
        catch (Exception ex) { App.Logger?.Debug("TcHostService.Close: {E}", ex.Message); }
        finally { _disposing = false; }
    }

    /// <summary>A graceful close the host asks for: <c>close {reason}</c>, then <see cref="Close"/> on the
    /// page's <c>exit-done</c> or at <see cref="TcHostRules.CloseForce"/>.</summary>
    public static void RequestClose(string reason)
    {
        if (_host == null) return;
        try { _host.Post(TcHostRules.Close(reason)); } catch (Exception ex) { Diag.Swallowed(ex); }
        StartCloseClock();
    }

    // ============================ page ============================

    private static void OnPageReady()
    {
        _lastAnyUtc = _lastBeatUtc = DateTime.UtcNow;
        // First launch with no cached Expo size: wait for the manifest read, at most 1.5 s from the
        // window opening, never on the UI thread. Past that, init goes out without the size.
        var hold = ExpoInitHold();
        if (hold > TimeSpan.Zero && _expoSizeRead is { } read)
        {
            var host = _host;
            _ = Task.WhenAny(read, Task.Delay(hold)).ContinueWith(_ => RunOnUi(() =>
            {
                if (host != null && ReferenceEquals(host, _host)) PostInit();
            }), TaskScheduler.Default);
            return;
        }
        PostInit();
    }

    private static void PostInit()
    {
        try
        {
            var s = App.Settings?.Current;
            var caps = TcRules.Caps(_edition, saveSlot: SaveSlotOpen, ExpoCap());
            var init = TcHostRules.InitFrame(Localization.LocalizationManager.Instance.CurrentLanguage,
                s?.MotionLevel ?? Models.MotionLevel.Full, Safety.GentleVisuals.On, App.Mods?.ActiveModId, caps,
                picturesOn: PicturesOnNow, sparkles: App.Sparkles != null && !string.IsNullOrEmpty(App.UnifiedUserId),
                toys: true, toysConnected: ToysReady);
            _host?.Post(init);
        }
        catch (Exception ex) { App.Logger?.Warning("Tc: init post failed: {E}", ex.Message); }
        _host?.FocusWeb();
    }

    private static void OnPageMessage(JObject o)
    {
        _lastAnyUtc = DateTime.UtcNow;
        var gate = TcFrameGate.Check(o);
        if (!gate.Accepted || gate.Frame is not { } f)
        {
            App.Logger?.Debug("Tc: page frame refused ({Type}: {Why})", (string?)o["type"], gate.Refusal);
            // A save over the caps still gets its answer (the page shows the card's reason).
            if ((string?)o["type"] == "save-offer")
                PostSave(TcSaveRules.Result(false, gate.Refusal == TcFrameGate.RefuseTooBig ? TcSaveRules.ReasonTooBig : TcSaveRules.ReasonRefused));
            return;
        }
        try
        {
            switch (f.Type)
            {
                case "heartbeat":
                    _lastBeatUtc = _lastAnyUtc;
                    break;
                case "save-offer":
                    OnSaveOffer(f);
                    break;
                case "save-ask":
                    OnSaveAsk();
                    break;
                case "boot-error":
                    App.Logger?.Warning("Tc: page boot-error: {Msg}", f.Save);
                    RunOnUi(Close);
                    break;
                case "exit":
                    // The page's own Leave: it winds itself down and says exit-done. Escape never sends this.
                    StartCloseClock();   // web messages arrive on the UI thread
                    break;
                case "exit-done":
                    if (_closeAskedUtc != null) RunOnUi(Close);
                    break;
                case "open-url":
                    // Already on the contract's allow list (TcFrameGate.OpenUrl).
                    RunOnUi(() => Helpers.BrowserLauncher.TryOpenUrl(f.Url));
                    break;
                case "haptic":
                    OnHaptic(f);
                    break;
                case "nudge":
                    // Desktop: every nudge is the tier compare card (Contract v2).
                    RunOnUi(OpenTierCard);
                    break;
                case "set-setting":
                    RunOnUi(() => OnPictureSetting(f));
                    break;
                case "scrolller-niches":
                    RunOnUi(() => OnPictureNiches(f));
                    break;
                case "fuzz-ended":
                    OnRunEnded(f);
                    break;
                case "achievement":
                    OnAchievement(f);   // sticker Sparkles (TcHostService.Stickers.cs)
                    break;
                case "own-media-list":
                    RunOnUi(OnOwnMediaList);   // the app's own pictures for "Use local files" (caps.ownMedia.library)
                    break;
                case "expo-fetch":
                    OnExpoFetch();   // the page sends it only from a press
                    break;
                case "expo-cancel":
                    StopExpo();
                    break;
                default:
                    App.Logger?.Debug("Tc: {Type} not handled by this build yet", f.Type);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Tc: message handler threw ({Type}): {E}", f.Type, ex.Message); }
    }

    /// <summary>The tier card over the game: the run holds (<c>suspend focus</c>) until the game window
    /// is active again. A patron whose grant died on this PC gets Reconnect instead (TierGate's split).</summary>
    private static void OpenTierCard()
    {
        if (_host == null) return;
        try
        {
            _host.Post(TcHostRules.Suspend(true, TcHostRules.ReasonFocus));
            _focusHeld = true;
            StopPulse();      // the run is held: the toy goes quiet with it
            if (TierGate.ReconnectIsTheAnswer()) App.MainWindowRef?.StartPatreonReconnectFromGate();
            else App.MainWindowRef?.ShowVaultGate(null, 1);
        }
        catch (Exception ex) { App.Logger?.Warning("Tc: tier card failed: {E}", ex.Message); }
    }

    private static void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_focusHeld || _host == null) return;
        _focusHeld = false;
        try { _host.Post(TcHostRules.Suspend(false, TcHostRules.ReasonFocus)); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    // ============================ clocks ============================

    private static void StartCloseClock()
    {
        if (_host == null || _closeAskedUtc != null) return;
        _closeAskedUtc = DateTime.UtcNow;
        _closeClock = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _closeClock.Tick += (_, _) =>
        {
            if (_closeAskedUtc is { } asked && TcHostRules.CloseNow(false, DateTime.UtcNow - asked)) Close();
        };
        _closeClock.Start();
    }

    private static void StartWatch()
    {
        _watch = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
        _watch.Tick += (_, _) =>
        {
            if (_host == null) return;
            var now = DateTime.UtcNow;
            if (TcHostRules.Watch(_host.IsReady, now - _lastAnyUtc, now - _lastBeatUtc) == TcWatchVerdict.Close)
            {
                App.Logger?.Warning("Tc: page silent (ready={Ready}) - closing", _host.IsReady);
                Close();
            }
        };
        _watch.Start();
    }

    private static void RunOnUi(Action act)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { try { act(); } catch (Exception ex) { Diag.Swallowed(ex); } return; }
        if (disp.HasShutdownStarted) return;
        disp.BeginInvoke(act, DispatcherPriority.Normal);
    }
}
