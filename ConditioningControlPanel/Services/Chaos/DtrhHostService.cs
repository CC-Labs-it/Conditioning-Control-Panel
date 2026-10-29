using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Host service for the DtRH browser game (feat/dtrh-web-port). Owns the fullscreen
/// input-receiving WebView2 window (via <see cref="ChaosWebViewHost"/>), the virtual-host
/// mappings (ccp.game -> Resources/web, ccp.assets -> the user's active preset), and the
/// bridge protocol (v1):
///   - fire-payload -> desktop audio payloads only (EffectPayloadFactory; DtrhNativePayloadRule refuses video)
///   - meta-command -> <see cref="DtrhMetaBridge"/> (chaos_meta.json stays C#-owned)
///   - run-started/run-ended -> crash sentinel, barks, XP payout + payout-result reply
///   - payload-state (video) -> page pauses while a mandatory video covers it; focus returns after
///   - heartbeat watchdog + ProcessFailed relaunch-once recovery
///
/// Deliberately NOT an evolution of ChaosTunnelService - opposite window semantics (the
/// tunnel is a passive backdrop under the WPF game; this IS the game surface). The two
/// coexist until the legacy WPF game retires.
/// </summary>
internal static class DtrhHostService
{
    private const int Protocol = 1;
    private static ChaosWebViewHost? _host;
    private static DtrhMetaBridge? _meta;
    private static DispatcherTimer? _exitWatchdog;
    private static DispatcherTimer? _heartbeatWatch;
    private static DateTime _lastHeartbeatUtc;

    /// <summary>When this trip down the hole started, for EMI's <c>dtrhClosed</c> duration. Nothing
    /// else here was timing the visit. MinValue means "not open", which is also what keeps the
    /// reentrant teardown from announcing the same visit twice.</summary>
    private static DateTime _emiOpenedUtc = DateTime.MinValue;

    private static bool _exiting;
    private static bool _runActive;
    private static bool _minimizedMainWindow;   // we tucked the main window to the tray for this session
    private static bool _relaunchedOnce;
    private static bool _disposing;   // reentrancy guard: _host.Dispose() closes the window -> Closed -> DisposeAll
    private static bool _testMode;
    private static bool _videoHooked;
    private static bool _vnSpeaking;   // a VN tutorial beat owns the mix: skip native stingers/barks
    private static bool _worldFrozen;  // an in-world Freeze bubble is holding native video + voice
    private static bool _diveMuted;    // the dive's in-page master mute is holding native video silent

    // ---- per-run native engagement metrics (local-only session telemetry) ----
    // Durations only the host can measure (video watch / voiceover / native audio
    // subliminals) accumulate here between run-started and run-ended, then merge into
    // the page's sessionStats snapshot and sum into DtrhSessionStatsStore at OnRunEnded.
    private static double _runVideoWatchSec;
    private static int _runVideosShown;
    private static int _runVideosSkipped;
    private static int _runVoicelines;
    private static double _runVoiceoverSec;
    private static int _runSubliminalsHeard;

    /// <summary>The page's last run-progress snapshot: the run-ended body as it stood a few
    /// seconds ago. Banked by <see cref="DisposeAll"/> when the window dies with a descent still
    /// falling, which is the one exit the page cannot report. See <see cref="DtrhRunCloseRule"/>.</summary>
    private static JObject? _lastRunProgress;

    /// <summary>When the host saw run-started. The host has no clock inside the descent, so every
    /// figure a payout rests on otherwise comes from the page - including the one that is worth
    /// Sparks. This bounds it. MinValue means no descent has started this window.</summary>
    private static DateTime _runStartedUtc = DateTime.MinValue;

    /// <summary>Seconds since run-started by the host's own clock, or null when it never saw
    /// one.</summary>
    private static double? HostElapsedSec => _runStartedUtc == DateTime.MinValue
        ? null
        : (DateTime.UtcNow - _runStartedUtc).TotalSeconds;

    public static bool IsActive => _host != null;

    private static void PostManifest()
    {
        var m = DtrhAssetManifest.Build();
        DtrhModContent.MergeMedia(m);   // creator mods: mix/replace descent media
        _host?.Post(new
        {
            type = "manifest",
            images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
            videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
            skipped = m.Skipped,
            truncated = m.Truncated,
        });
    }

    private static void OnRemoteCopiesReady()
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { if (_host != null && _host.IsReady && !_exiting) PostManifest(); }
                catch (Exception ex) { App.Logger?.Debug("DtrhHost: manifest re-post failed: {E}", ex.Message); }
            }));
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost: copies-ready dispatch failed: {E}", ex.Message); }
    }

    /// <summary>The game window is up AND in front: the panic key's Escape is the game's pause then
    /// (PanicPolicy.GameClaimsEscapeAsPause), as it is for the race and the chess board.</summary>
    public static bool IsInFront => _host?.IsForeground == true;

    /// <summary>True once the page has said <c>ready</c> and is not on its way out.</summary>
    public static bool IsReady => _host?.IsReady == true && !_exiting;

    /// <summary>Hand the page an Escape the panel kept as its pause (engine/scene.js plays it once
    /// unless the real key reached it too), and give it the keyboard back.</summary>
    public static void PostKeptEscape()
    {
        try
        {
            _host?.Post(new { type = CaucusHostService.KeptEscapeType });
            _host?.FocusWeb();
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost: kept Escape post failed: {E}", ex.Message); }
    }

    /// <summary>A DtRH descent is currently running (between run-started and run-ended). Used to
    /// cheaply gate optional per-run telemetry off the global bark/audio paths.</summary>
    public static bool IsRunActive => _runActive;

    /// <summary>The page reported boot-error (WebGL/GPU init failed) this app session.
    /// The Lab entry points check this and route to the classic WPF game instead, so a
    /// machine that can't run the browser game isn't stuck clicking into a black flash.</summary>
    public static bool BootFailedThisSession { get; private set; }

    /// <summary>Launch the game window (idempotent). The page boots into The Fall on the
    /// active preset; the Warren hub becomes the boot target in M5.</summary>
    public static void Launch(bool testMode = false)
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            // EMI Desk: the ring learns from every open, not just its own cards.
            try { App.EmiDesk?.NoteOpen("dtrh"); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { App.EmiDesk?.Fire("dtrhOpened", null); } catch (Exception ex) { Diag.Swallowed(ex); }
            _emiOpenedUtc = DateTime.UtcNow;

            // The descent's audio (bubbles sfx, vn shared, drone bed) ships as the lazy audio-web
            // pack. Fire-and-forget: no-op once installed, on a full install, or offline.
            try { _ = App.ReleaseContent?.RequestPackAsync(ReleaseContentService.PackAudioWeb); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost: audio-web request failed: {E}", ex.Message); }

            _exiting = false;
            _runActive = false;
            _lastRunProgress = null;
            _runStartedUtc = DateTime.MinValue;
            _worldFrozen = false;
            _testMode = testMode;
            _meta = new DtrhMetaBridge(testMode, msg => _host?.Post(msg));
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            // THE LOOM (crafting Part 2): saved spirals live in the CCP Spirals library
            // folder, exposed to the page as ccp.spirals. A missing folder would make
            // WebView2 SKIP the mapping entirely, so create it BEFORE Launch.
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost: spirals dir create failed: {E}", ex.Message); }
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // Allow (not DenyCors): the engine uploads this media to WebGL, which
                // needs CORS-clean responses (verified in the M0 spike).
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                // M4: the bundled Chaos art (bubble sprites, boon icons, announcer
                // banners). Allow (not DenyCors): the in-tube boon draft
                // (engine/boonPick.js) uploads the boon icons to WebGL, which needs
                // CORS-clean responses; plain <img> consumers are unaffected.
                ("ccp.art", Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"), CoreWebView2HostResourceAccessKind.Allow),
                // THE LOOM: the saved-spiral GIFs (thumbnails + in-run overlay pool).
                ("ccp.spirals", DtrhLoomStore.SpiralsFolder, CoreWebView2HostResourceAccessKind.Allow),
                // CONTENT PACKS: downloaded audio (barks, drone, bubbles sfx, vn vo) that no longer
                // ships in the installer. Mirrors the ccp.game tree, so the page's audio shim only
                // swaps the origin. Folder is created by ContentMapping() - a missing one would be
                // skipped, and then the shim's fallback would never find anything.
                ChaosWebViewHost.ContentMapping(),
            };
            // Creator mods: an installed mod's resources/dtrh folder (voice clips,
            // descent media, portrait, drone). Mapping ONLY the dtrh subfolder keeps
            // the rest of the mod's resources off the page. Launch-time snapshot:
            // switching the active mod requires a DTRH restart (ActivateMod closes us).
            var modDtrh = DtrhModContent.ModDtrhRoot();
            if (modDtrh != null)
                mappings.Add(("ccp.mod", modDtrh, CoreWebView2HostResourceAccessKind.Allow));
            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/index.html",
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "browser_data_dtrh",
                InputEnabled = true,
                IsGame = true,   // the startup ladder goes quiet while a game is up
                // Launch in a normal titled window (Alt-Tab / minimize / move); the dock's [ ]
                // button toggles borderless fullscreen. All effects are in-world now, so nothing
                // needs the old topmost fullscreen surface.
                StartFullscreen = false,
                // Glue the descent above MainWindow via native ownership: main gets raised by
                // plenty of things we don't control (avatar barks, a video window closing, a tray
                // restore) and used to land on top of the game. Ownership makes the window manager
                // keep the pair in order — without Topmost, which would cover other apps too.
                OwnedByMainWindow = true,
                WindowTitle = "Down the Rabbit Hole",
                LogTag = "DtrhHost",
                // The game's audio bed / drift voice must start without a click.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });
            _host.Show();
            // Windowed game: the user closes it via the title-bar X. Tear down cleanly so the
            // heartbeat watchdog can't misread the resulting heartbeat silence as a mid-run crash
            // and relaunch the window (mirrors IntakeHostService / BureauHostService).
            if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
            HookVideoEvents(true);
            StartHeartbeatWatch();
            Haptics.DtrhHapticDirector.OnLaunch(testMode);
            // Tuck the main CCP window into the tray while the descent owns the screen;
            // DisposeAll restores it on exit. The game window keeps focus.
            try
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    mw.MinimizeToTrayForChaos();
                    _minimizedMainWindow = true;
                    _host.FocusWeb();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost: minimize main window failed: {E}", ex.Message); }
            App.Logger?.Information("DtrhHostService: launched{T}", testMode ? " (M2 TEST MODE)" : "");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "DtrhHostService.Launch failed");
            CloseActive();
        }
    }

    /// <summary>Graceful close: ask the page to wind down, watchdog-force after 1200ms.
    /// Also the panic-key path. Idempotent.</summary>
    public static void CloseActive()
    {
        try
        {
            if (_host == null) return;
            if (_host.IsReady && !_exiting)
            {
                _exiting = true;
                _host.Post(new { type = "end-run", reason = "host" });
                ArmExitWatchdog();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    // ============================ boot ============================

    private static void OnPageReady()
    {
        try
        {
            _lastHeartbeatUtc = DateTime.UtcNow;
            // Keyboard focus does not land in the WebView2 child on a fresh launch until a
            // click - claim it now so Esc (pause / hold-to-exit) works from the first frame.
            _host?.FocusWeb();
            _host?.Post(new
            {
                type = "init",
                protocol = Protocol,
                settings = new { masterVolume = SafeMasterVolume() },
                // The player's Motion setting (full / reduced / off) for shared/motion.js.
                motionLevel = DtrhMotionWire.Current(),
                // Active persona (builtin-bambisleep / builtin-sissyhypno / builtin-locked):
                // the page's VN portrait picks the matching portrait set + tint.
                modId = SafeActiveModId(),
                // Creator mods: the mod's own DTRH content (drift pools, portrait,
                // tint, drone) as ccp.mod URLs; null = no mod content, page runs
                // on its shipped assets exactly as before.
                modContent = DtrhModContent.BuildInitPayload(),
                // M5: the page boots into the Warren hub; a run's config is dealt
                // per-descent by request-run. init carries the SAVED run setup so
                // the hub's Descent tab opens on the user's own choices.
                runSetup = DtrhRunWire.RunSetup(App.Settings?.Current),
                m2Test = _testMode,
                // The plan list (contract C1, PlanCatalog) for the run summary's end card. The
                // page shows a price only when this is present.
                plans = Vault.PlanCatalog.ToWire(Vault.PlanCatalog.Current),
            });
            _ = Vault.PlanCatalog.GetAsync();   // warm the cache for the next launch; never awaited here
            if (_meta != null) _host?.Post(_meta.SnapshotMessage());
            PostManifest();
            // Online media copied to this machine after this frame reaches the page as a fresh
            // manifest (the page replaces its pools), so the tube fills without a relaunch.
            DtrhAssetManifest.RemoteCopiesReady -= OnRemoteCopiesReady;
            DtrhAssetManifest.RemoteCopiesReady += OnRemoteCopiesReady;
            PostLoomList();   // THE LOOM: seed the page's saved-spiral pool
            // THE BIOMES (S3 read-back): the cumulative engagement ranking, so the
            // page can bias toward the media the user actually likes (the exact
            // future feature DtrhAssetStatsStore was built to serve).
            try
            {
                var favorites = DtrhAssetStatsStore.TopAssets(12);
                if (favorites.Count > 0) _host?.Post(new { type = "favorites", names = favorites });
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHostService favorites post failed: {E}", ex.Message); }
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHostService.OnPageReady: {E}", ex.Message); }
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "vn-speaking":
                _vnSpeaking = (bool?)o["on"] ?? false;
                break;
            case "sfx":
            {
                if (_vnSpeaking) break;   // VN owns the mix - stingers stay silent while she speaks
                var name = (string?)o["name"];
                var scale = (float?)o["scale"] ?? 0.6f;
                // Two cues live behind fallback-resolving helpers (no dedicated asset yet).
                if (name == "wave_clear") ChaosSfx.PlayWaveClear();
                else if (name == "ripple_cast") ChaosSfx.PlayRippleCast();
                else if (!string.IsNullOrEmpty(name)) ChaosSfx.Play(name, scale);
                break;
            }
            case "fire-payload":
                FirePayload(o);
                break;
            case "freeze-state":
                ApplyWorldFreeze((bool?)o["on"] ?? false);
                break;
            case "mute-state":
                ApplyDiveMute((bool?)o["on"] ?? false);
                break;
            case "meta-command":
                _meta?.Handle(o);
                break;
            case "request-run":
                OnRequestRun(o);
                break;
            case "run-progress":
                // The page's ~10s snapshot of the descent in flight, kept for one purpose: a
                // window that dies without a word (the frame's X, Alt+F4, a crash) has nobody
                // left to send run-ended, and the teardown banks this instead of losing the run.
                if (_runActive) _lastRunProgress = o;
                break;
            case "run-started":
            {
                _runActive = true;
                _lastRunProgress = null;
                _runStartedUtc = DateTime.UtcNow;   // the host's only clock inside a descent
                SeasonRecapService.TrackFeature(SeasonFeatureKeys.Dtrh);
                _vnSpeaking = false;   // never carry a stale duck into a run
                ApplyWorldFreeze(false);   // a stale freeze from a crashed prior run must not bleed into this descent's dedup state
                ResetRunMetrics();     // fresh native engagement counters for this descent
                var diff = (string?)o["difficulty"] ?? "Gentle";
                if (!_testMode)
                {
                    try { ChaosCrashSentinel.Mark($"mode=dtrh-web diff={diff}"); } catch (Exception ex) { Diag.Swallowed(ex); }
                    try { App.Bark?.NotifyChaosRunStarted(diff); } catch (Exception ex) { Diag.Swallowed(ex); }
                }
                try { Haptics.DtrhHapticDirector.OnRunStarted(); } catch (Exception ex) { Diag.Swallowed(ex); }
                App.Logger?.Information("DtrhHost: run started (diff={D}, mode={M})", diff, (string?)o["mode"]);
                break;
            }
            case "run-ended":
                OnRunEnded(o);
                break;
            case "bark":
                // Haptics tap FIRST: the moment happened whether or not a voice line plays
                // over it (the vn-speaking gate below only guards the audio mix).
                try { Haptics.DtrhHapticDirector.OnGameEvent(o); } catch (Exception ex) { Diag.Swallowed(ex); }
                if (_vnSpeaking) break;   // don't let a bark talk over her VN line
                RouteBark(o);
                break;
            case "haptic-state":
                // page's ~2s depth/melt feed for the haptic director's ambient floor
                try { Haptics.DtrhHapticDirector.OnHapticState(o); } catch (Exception ex) { Diag.Swallowed(ex); }
                break;
            case "heartbeat":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case "asset-stats":
                // per-asset engagement delta (weighted attention + paddle interactions);
                // summed into dtrh_asset_stats.json for future media-selection features
                try { DtrhAssetStatsStore.Merge(o); } catch (Exception ex) { Diag.Swallowed(ex); }
                break;
            case "loom-save":
            {
                // THE LOOM (crafting Part 2): the pane wove a GIF; store validates + writes
                // into the CCP Spirals library, then the page gets the verdict + fresh list.
                var (ok, slug, error) = DtrhLoomStore.Save(
                    (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                _host?.Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-delete":
            {
                var slug = (string?)o["slug"];
                var (ok, error) = DtrhLoomStore.Delete(slug);
                _host?.Post(new { type = "loom-result", op = "delete", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-reveal":
            {
                // 📂 on a rack tile: show the saved gif in Explorer (the game runs
                // fullscreen, so Explorer opens behind it - still there on alt-tab).
                var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                if (path != null)
                {
                    try { Helpers.ExplorerLauncher.RevealInExplorer(path); }
                    catch (Exception ex) { App.Logger?.Debug("DtrhHost: reveal failed: {E}", ex.Message); }
                }
                break;
            }
            case "boot-error":
                OnBootError((string?)o["msg"]);
                break;
            case "report-bug":   // the hub dock's 🐛 button (no title bar to hang one on)
                OpenBugReport();
                break;
            case "fullscreen-set":   // page's Esc ladder / dock button: C# owns the borderless toggle
                ApplyHostFullscreen((bool?)o["on"] ?? false);
                break;
            case "exit":       // page-initiated (Esc held): it winds itself down, then exit-done
                _exiting = true;
                ArmExitWatchdog();
                break;
            case "exit-done":
                DisposeAll();
                break;
            case "pong":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case Funnel.FunnelPageMessage.Type:   // the run summary's end card (contract C10)
                // Counted always (aggregate only); a checkout never opens over a running descent
                // or from the capture rig.
                if (!_testMode) Funnel.FunnelPageMessage.Handle(o, mayOpen: !_runActive && !_exiting);
                break;
        }
    }

    /// <summary>THE LOOM: post the saved-spiral library (page-ready + after save/delete).
    /// Params sidecars ride along so the pane can re-edit a kept spiral.</summary>
    private static void PostLoomList()
    {
        try
        {
            _host?.Post(new
            {
                type = "loom-list",
                spirals = DtrhLoomStore.List().Select(s => new
                {
                    slug = s.Slug,
                    url = $"https://ccp.spirals/loom_{s.Slug}.gif",
                    @params = TryParseJson(s.ParamsJson),
                }),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost.PostLoomList: {E}", ex.Message); }
    }

    private static JObject? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch { return null; }
    }

    /// <summary>Page-driven fullscreen: the game asks C# to borderless-toggle its own
    /// window (we do NOT use the browser Fullscreen API, which would hijack Esc). Echo the
    /// resulting window state back so the page's dock button + Esc ladder stay in sync.</summary>
    private static void ApplyHostFullscreen(bool on)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                _host?.SetFullscreen(on);
                _host?.Post(new { type = "fullscreen", on = _host.IsFullscreen });
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.fullscreen: {E}", ex.Message); }
        });
    }

    /// <summary>The hub's dock 🐛 button: this window has a native (buttonless) title bar,
    /// so the bug affordance lives in-game and posts here. Opens the same modal dialog the
    /// rest of the app uses (MainWindow's title-bar 🐛), owned by the DtRH window so it sits
    /// on top even in fullscreen.</summary>
    private static void OpenBugReport()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                var dlg = new BugReportWindow();
                var owner = _host?.Window;
                if (owner != null && owner.IsLoaded) dlg.Owner = owner;
                dlg.ShowDialog();
            }
            catch (Exception ex) { App.Logger?.Warning("DtrhHost.report-bug: {E}", ex.Message); }
        });
    }

    /// <summary>request-run {setup?} -> persist the hub's Descent-tab choices into AppSettings
    /// (the same fields ChaosHubWindow.SaveToSettings writes, so WPF and web share one setup),
    /// then deal a fresh run config: the scripted first-descent classroom when RunsCompleted == 0
    /// (ChaosHappyPath.BuildFirstRunConfig - naked, gentle, treats only), else FromSettings with
    /// the loadout pre-applied. Answered with {type:'run-config'}.</summary>
    private static void OnRequestRun(JObject o)
    {
        try
        {
            if (o["setup"] is JObject setup) DtrhRunWire.PersistRunSetup(setup, App.Settings?.Current);
            var (cfg, scripted, forceSpent) = DtrhRunWire.Deal(_testMode);
            if (forceSpent)
            {
                // Persist + rebroadcast so JS agrees (the one-shot was spent at deal time).
                try { _meta?.Rebroadcast(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            _host?.Post(new { type = "run-config", runConfig = DtrhRunWire.RunConfig(cfg, App.Settings?.Current) });
            App.Logger?.Information("DtrhHost: dealt run config (diff={D}, scripted={S})", cfg.Difficulty, scripted);
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.OnRequestRun: {E}", ex.Message); }
    }

    /// <summary>fire-payload {kind, strength?, durationMult?} -> a native effect.
    /// Since the 2026-07 hard cutover the browser game renders every VISUAL effect in-world
    /// (dtrh/game/payloadFx.js) - no more native WPF layered windows over the game surface.
    /// Only audio still rides this bridge: a one-shot whisper on the native audio path. The
    /// video card is in-world too, and a native video here would outlive the run
    /// (<see cref="DtrhNativePayloadRule"/>, ccp-bugs #1470).
    /// Any other kind arriving here means a page/host version mismatch - log and ignore.</summary>
    private static void FirePayload(JObject o)
    {
        try
        {
            var kindStr = (string?)o["kind"];
            if (string.IsNullOrWhiteSpace(kindStr)) return;

            if (!DtrhNativePayloadRule.Accepts(kindStr))
            {
                // Visual effects are in-world now; the page should never send them here.
                App.Logger?.Warning("DtrhHost: payload kind '{K}' is in-world since the cutover - ignored", kindStr);
                return;
            }

            EffectPayload payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Audio);
            if (_runActive) _runSubliminalsHeard++;   // native whisper = a subliminal heard

            payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
            payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);
            payload.Fire();
            App.Logger?.Information("DtrhHost: fired native payload {K} (strength {S})", payload.DisplayName, payload.Strength);
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.FirePayload: {E}", ex.Message); }
    }

    /// <summary>
    /// Whether a finished run was opened by the Daily Gift: today's server-named free key is
    /// <c>dtrh</c> and the account holds no Prime grant of its own. The capture rig never counts.
    /// Drives the <c>plan</c> / <c>daily_gift</c> fields of payout-result (contract C10).
    /// </summary>
    internal static bool RunCameFromDailyGift(bool testMode, bool hasLabAccess, bool dtrhFreeToday)
        => !testMode && !hasLabAccess && dtrhFreeToday;

    /// <summary>run-ended -> XP payout (C#-owned formula, identical to the WPF EndRun) +
    /// meta banking via the shared AwardRunRewards, answered with payout-result.</summary>
    /// <param name="fromTeardown">The host synthesised this booking from its last progress
    /// snapshot because the window died. The crash sentinel is NOT cleared on that path: a
    /// WebView2 process failure reaches the same teardown, and clearing it would report a real
    /// crash as a clean run on the next launch.</param>
    private static void OnRunEnded(JObject o, bool fromTeardown = false)
    {
        // The host is the authority on whether a descent is still open, because it now has TWO
        // producers: the page's run-ended and the teardown's synthetic one. A run-ended already
        // queued on the dispatcher can be pumped after DisposeAll banked, and paying it again
        // would double the Sparks and the run counter.
        bool wasActive = _runActive;
        var hostElapsed = HostElapsedSec;   // read before the claim clears the stamp
        _runActive = false;
        _lastRunProgress = null;   // banked: nothing left for the teardown to pay
        _runStartedUtc = DateTime.MinValue;
        try { Haptics.DtrhHapticDirector.OnRunEnded(); } catch (Exception ex) { Diag.Swallowed(ex); }
        ApplyWorldFreeze(false);   // a run ending mid-freeze must resume native video + voice, not wedge them through the hub
        if (!DtrhRunCloseRule.ShouldPayBooking(wasActive))
        {
            App.Logger?.Debug("DtrhHost: run-ended for a descent that is already banked - ignored");
            return;
        }
        try
        {
            // A descent that was LEFT is paid for the seconds it really fell, and only counts as
            // a run once it lasted a minute (DtrhRunWire.Book / DtrhRunPayoutRule).
            var booking = DtrhRunWire.Book(o, hostElapsed);
            string diff = booking.Difficulty;
            double baseXp = booking.BaseXp;
            double skillMult = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            double finalXp = baseXp * skillMult;

            // SPARKLES BY PLACE (B1): a descent that reached its own ending claims a medal against
            // this run's XP ceiling. A left descent pays nothing (owner: quitting early pays nothing).
            if (!abandoned && !_testMode)
            {
                var slipHost = _host;
                _ = Sparkles.SparklesSlip.Deliver(Sparkles.SparklesGameEnd.Claim("dtrh", "solo",
                    Sparkles.SparklesResult.ForMedal(Sparkles.SparklesGameEnd.RatioMedal(score, capBase)),
                    TimeSpan.FromSeconds(durationSec), new JObject { ["difficulty"] = diff }),
                    json => slipHost?.Post(new JRaw(json)));
            }

            long previousBest = _meta?.TestMode == true ? 0 : ChaosMeta.State.BestScore;

            int sparksEarned = 0;
            if (_meta != null)
            {
                sparksEarned = _meta.AwardRun(booking.RewardInput(o), booking.Payout.CountsAsRun);
            }

            ChaosRank? rankUp = null;
            if (!_testMode)
            {
                try { App.Progression?.AddXP(baseXp, XPSource.Chaos); }
                catch (Exception ex) { App.Logger?.Debug("DtrhHost payout AddXP: {E}", ex.Message); }
                // Restore V1 behavior: a run's popped bubbles feed the GLOBAL bubble
                // count and its per-100 sparkle-point milestones. The web port had been
                // recording bubblesPopped only into the local stats store; this credits
                // it to the same sink the native chaos mode uses. Additive to score XP.
                try
                {
                    int bubblesPopped = (int?)(o["sessionStats"]?["bubblesPopped"]) ?? 0;
                    if (bubblesPopped > 0) App.Achievements?.TrackBubblesPopped(bubblesPopped);
                }
                catch (Exception ex) { App.Logger?.Debug("DtrhHost bubble credit: {E}", ex.Message); }
                try { RevealService.Sync("run_end"); } catch (Exception ex) { Diag.Swallowed(ex); }
                try
                {
                    var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
                    if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                // Not on the teardown path: the window is closing, so a "nice descent" line has
                // nowhere to land and arrives over whatever the player went back to. The reveal
                // sync above is bookkeeping and still runs.
                if (!fromTeardown)
                {
                    try { App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff); } catch (Exception ex) { Diag.Swallowed(ex); }
                }
                // NOT on the teardown path: ChaosCrashSentinel.Recover("process-failed") and
                // ("heartbeat-silent") both land in DisposeAll, and clearing the sentinel from
                // there would report a genuine WebView2 crash as a clean run next launch.
                if (!fromTeardown) { try { ChaosCrashSentinel.Clear(); } catch (Exception ex) { Diag.Swallowed(ex); } }
                // RevealService.Sync mutated pendingReveals BEHIND the bridge - push a fresh
                // snapshot so the Warren's flash pass sees the new pendings on return.
                try { _meta?.Rebroadcast(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // Local-only session telemetry: fold the host-measured natives + payout into the
            // page's sessionStats snapshot and sum it into the cumulative store. Best-effort,
            // never sent to the server. (No recap UI reads this yet - that ships with the
            // unlock-gated recap card later; we just accumulate the data now.)
            try
            {
                var js = o["sessionStats"] as JObject ?? new JObject();
                js["videoWatchSec"]    = _runVideoWatchSec;
                js["videosShown"]      = _runVideosShown;
                js["videosSkipped"]    = _runVideosSkipped;
                js["voicelinesHeard"]  = _runVoicelines;
                js["voiceoverSec"]     = _runVoiceoverSec;
                js["subliminalsHeard"] = _runSubliminalsHeard;
                js["sparksEarned"]     = sparksEarned;
                js["xpEarned"]         = finalXp;
                var life = DtrhSessionStatsStore.Record(js, diff);
                App.Logger?.Information(
                    "DtrhHost: session metrics recorded (run #{Runs}): {Bubbles} bubbles, {Boons} boons, {VidSec:0}s video, {VoxSec:0}s voice",
                    life.Runs, life.BubblesPopped, life.BoonsReceived, life.VideoWatchSec, life.VoiceoverSec);
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost session-metrics record: {E}", ex.Message); }

            bool dailyGift = RunCameFromDailyGift(_testMode, App.Patreon?.HasLabAccess == true,
                App.DailyFree?.IsFreeToday("dtrh") == true);
            _host?.Post(new
            {
                type = "payout-result",
                baseXp,
                skillMult,
                finalXp,
                sparksEarned,
                previousBest,
                rankUp = rankUp?.ToString(),
                dryRun = _testMode,
                // End card (contract C10): a run opened by the Daily Gift, not by a Prime grant,
                // gets "Open today. Prime keeps it open." under the result.
                plan = dailyGift ? "prime" : null,
                daily_gift = dailyGift,
            });
            App.Logger?.Information(
                "DtrhHost: web run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks{T}",
                baseXp, skillMult, finalXp, sparksEarned, _testMode ? " (TEST, no XP credited)" : "");
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhHost.OnRunEnded: {E}", ex.Message); }
    }

    /// <summary>bark {event, ...} -> the matching App.Bark chaos hook (voice stays native).
    /// The FULL M4 surface: the page mirrors every WPF Notify* call site; BarkService's
    /// own cooldown/weighting logic decides what actually speaks.</summary>
    private static void RouteBark(JObject o)
    {
        if (_testMode) return;
        try
        {
            var bark = App.Bark;
            if (bark == null) return;
            string S(string k, string d = "") => (string?)o[k] ?? d;
            int I(string k) => (int?)o[k] ?? 0;
            double D(string k) => (double?)o[k] ?? 0;
            switch ((string?)o["event"])
            {
                case "ending-soon": bark.NotifyChaosEndingSoon(); break;
                case "wave-cleared": bark.NotifyChaosWaveCleared(I("wave")); break;
                case "wave-escalated": bark.NotifyChaosWaveEscalated(I("wave")); break;
                case "act-changed": bark.NotifyChaosActChanged(I("act"), I("wave")); break;
                case "benign-popped": bark.NotifyChaosBenignPopped(S("variant"), S("payload"), I("combo")); break;
                case "defused": bark.NotifyChaosBubbleDefused(I("combo"), S("variant"), S("difficulty")); break;
                case "detonated": bark.NotifyChaosBubbleDetonated(S("variant"), D("strength"), D("runDetonations"), I("combo"), S("difficulty")); break;
                case "detonated-absorbed": bark.NotifyChaosBubbleDetonatedAbsorbed(S("variant"), D("strength"), D("runDetonations"), I("combo"), S("difficulty"), I("shields")); break;
                case "darter-caught": bark.NotifyChaosDarterCaught(D("points"), I("combo"), (bool?)o["quick"] ?? false); break;
                case "freeze-caught": bark.NotifyChaosFreezeCaught(D("points"), I("combo")); break;
                case "combo-milestone": bark.NotifyChaosComboMilestone(I("combo"), S("difficulty")); break;
                case "combo-big": bark.NotifyChaosComboBig(I("combo"), D("threshold")); break;
                case "boon-picked": bark.NotifyChaosBoonPicked(S("name")); break;
                case "curse-picked": bark.NotifyChaosCursePicked(S("name"), S("rarity"), D("mult")); break;
                case "boon-skipped": bark.NotifyChaosBoonSkipped(I("shields")); break;
                case "draft-autopick": bark.NotifyChaosDraftAutopick(); break;
                case "focus-low": bark.NotifyChaosFocusLow(); break;
                case "defuse-first": bark.NotifyChaosDefuseFirst(); break;
                case "defuse-nofocus": bark.NotifyChaosDefuseNoFocus(); break;
                case "defuse-release": bark.NotifyChaosDefuseRelease(); break;
                case "click-detonate": bark.NotifyChaosClickDetonate(); break;
                case "tease-debut": bark.NotifyChaosTeaseDebut(); break;
                case "tease-clicked": bark.NotifyChaosTeaseClicked(); break;
                case "tease-denied": bark.NotifyChaosTeaseDenied(I("count")); break;
                case "tease-denied-streak": bark.NotifyChaosTeaseDeniedStreak(I("count")); break;
                case "gold-first": bark.NotifyChaosGoldFirst(); break;
                // ---- M5: Warren hub + lessons + happy path ----
                case "dollhouse-first-open": bark.NotifyChaosDollhouseFirstOpen(); break;
                case "reveal-flash": bark.NotifyChaosRevealFlash(S("id")); break;
                case "lesson-complete": bark.NotifyChaosLessonComplete(S("id")); break;
                case "duo-demo": bark.NotifyChaosDuoDemo(); break;
                // ---- Wave 2: tunnel pickups (reuse the rabbit-catch voice) ----
                case "rabbit-caught": bark.NotifyChaosDarterCaught(I("gold"), 0, true); break;
                // ---- Crafting: THE BOUDOIR (reuse the first-time voice) ----
                case "crafted": bark.NotifyChaosFirstTime(S("id")); break;
                // Haptics-only: an in-world effect payload landed. The director taps this before
                // the routing switch (#1244); she has nothing to say about it, and saying so once
                // per flash would be a log line per flash.
                case "effect-fired": break;
                default: App.Logger?.Debug("DtrhHost: unrouted bark event '{E}'", (string?)o["event"]); break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhHost.RouteBark: {E}", ex.Message); }
    }

    // ============================ world freeze ============================

    /// <summary>freeze-state {on} - the in-world Freeze bubble stops the field, so stop the
    /// REAL world with it: pause any covering video and the currently-playing spoken voiceline
    /// for the freeze window, resuming both when it lifts. Idempotent (dedup on _worldFrozen);
    /// also force-resumed on run end / teardown so a clip can never wedge paused.</summary>
    /// <summary>mute-state {on} - the dive's in-page master mute is a WEB switch that can
    /// only silence page media; a mandatory native video that covers the page never heard it
    /// and faded its audio in anyway. Mirror the mute onto the native VideoService so one
    /// control silences everything. Idempotent; force-released on run end / teardown so a
    /// muted descent can never leave every video silent afterward.</summary>
    private static void ApplyDiveMute(bool on)
    {
        if (on == _diveMuted) return;
        _diveMuted = on;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            // Apply the CURRENT flag, not the value captured when this was queued (#1103). The
            // teardown below releases the mute synchronously, so a mute queued just before it
            // would otherwise land afterwards and leave every video for the rest of the process
            // silent - with nothing in the log to say why.
            try { App.Video?.SetExternalMute(_diveMuted); }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.ApplyDiveMute: {E}", ex.Message); }
        });
    }

    private static void ApplyWorldFreeze(bool on)
    {
        if (on == _worldFrozen) return;
        _worldFrozen = on;
        try { Haptics.DtrhHapticDirector.OnWorldFreeze(on); } catch (Exception ex) { Diag.Swallowed(ex); }
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                if (on)
                {
                    App.Video?.PausePrimary();
                    App.AvatarWindow?.PauseSpokenAudio();
                }
                else
                {
                    App.Video?.PlayPrimary();
                    App.AvatarWindow?.ResumeSpokenAudio();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("DtrhHost.ApplyWorldFreeze: {E}", ex.Message); }
        });
    }

    // ============================ native payload state ============================

    /// <summary>A mandatory video fully covers the game - tell the page (pause/duck) and
    /// give it Win32 focus back when the video closes (video clicks steal activation).</summary>
    private static void HookVideoEvents(bool on)
    {
        try
        {
            if (App.Video == null) return;
            if (on && !_videoHooked)
            {
                App.Video.VideoStarted += OnVideoStarted;
                App.Video.VideoEnded += OnVideoEnded;
                App.Video.VideoWatchCredited += OnVideoWatchCredited;
                _videoHooked = true;
            }
            else if (!on && _videoHooked)
            {
                App.Video.VideoStarted -= OnVideoStarted;
                App.Video.VideoEnded -= OnVideoEnded;
                App.Video.VideoWatchCredited -= OnVideoWatchCredited;
                _videoHooked = false;
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private static void OnVideoStarted(object? sender, EventArgs e)
    {
        if (_runActive) _runVideosShown++;   // session telemetry: a video was shown this run
        try { Haptics.DtrhHapticDirector.OnVideoCovering(true); } catch (Exception ex) { Diag.Swallowed(ex); }
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() => _host?.Post(new { type = "payload-state", kind = "video", on = true }));
    }

    /// <summary>The video path finished crediting its watched time (fires from VideoService's
    /// interruption-safe credit finalizer). Accumulate the real watched seconds and, when the
    /// user bailed well before the end, count it as a skip. Local-only session telemetry.</summary>
    private static void OnVideoWatchCredited(object? sender, VideoWatchInfoEventArgs e)
    {
        if (!_runActive) return;
        _runVideoWatchSec += Math.Max(0, e.WatchedSec);
        if (e.DurationSec > 0 && e.WatchedSec / e.DurationSec < 0.90) _runVideosSkipped++;
    }

    /// <summary>Zero the per-run native engagement counters (called on run-started).</summary>
    private static void ResetRunMetrics()
    {
        _runVideoWatchSec = 0;
        _runVideosShown = 0;
        _runVideosSkipped = 0;
        _runVoicelines = 0;
        _runVoiceoverSec = 0;
        _runSubliminalsHeard = 0;
    }

    /// <summary>A companion voiceline played its audio (called from BarkService.Speak with the
    /// clip's duration in seconds). No-ops unless a DtRH run is active, so instrumenting the
    /// global bark path costs nothing off the game. Local-only session telemetry.</summary>
    public static void NoteVoicelineHeard(double sec)
    {
        if (!_runActive) return;
        _runVoicelines++;
        if (sec > 0) _runVoiceoverSec += sec;
    }

    private static void OnVideoEnded(object? sender, EventArgs e)
    {
        try { Haptics.DtrhHapticDirector.OnVideoCovering(false); } catch (Exception ex) { Diag.Swallowed(ex); }
        var disp = Application.Current?.Dispatcher;
        if (disp == null || _host == null) return;
        disp.BeginInvoke(() =>
        {
            _host?.Post(new { type = "payload-state", kind = "video", on = false });
            _host?.FocusWeb();   // the video window had Win32 focus; reclaim keyboard for the game
        });
    }

    /// <summary>The page's 3D boot failed (a genuine WebGL/GPU failure - the page only
    /// reports boot-error after the engine import or renderer creation threw). Fall back
    /// to the classic WPF game for the rest of the session so the user's click still
    /// lands in a game.</summary>
    private static void OnBootError(string? msg)
    {
        App.Logger?.Warning("DtrhHost: page boot-error: {Msg} - falling back to the classic game this session", msg);
        BootFailedThisSession = true;
        bool wasTest = _testMode;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            DisposeAll();
            if (!wasTest) LaunchLegacyFallback();
        });
    }

    /// <summary>Mirror of the Lab card's legacy branch (MainWindow.Lab.cs) for the
    /// boot-error path: scripted first run when fresh, else the WPF hub.</summary>
    private static void LaunchLegacyFallback()
    {
        try
        {
            if (App.Chaos == null || App.Chaos.IsRunning) return;
            if (ChaosMeta.State.RunsCompleted == 0)
            {
                App.Chaos.StartRun(ChaosHappyPath.BuildFirstRunConfig());
                return;
            }
            if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
            var hub = new ChaosHubWindow();
            if (Application.Current?.MainWindow is Window mw && mw.IsLoaded) hub.Owner = mw;
            hub.Show();
        }
        catch (Exception ex) { App.Logger?.Error(ex, "DtrhHost: legacy fallback failed"); }
    }

    // ============================ watchdogs / recovery ============================

    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Guarded on IsReady: the page only starts beating (rAF, ~2s) after boot, so a
            // still-loading page can't false-trip. A wedged live RUN gets the fast ladder;
            // the idling Warren gets a lazier one - but it must exist, because a locked
            // page main thread also kills the JS Esc-hold exit and would otherwise trap
            // the user under a fullscreen topmost window (panic key aside).
            if (_host == null || !_host.IsReady || _exiting) return;
            double silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            double limit = _runActive ? 10 : 20;
            if (silent > limit)
            {
                App.Logger?.Warning("DtrhHost: page heartbeat silent >{Limit}s ({Where}) - recovering",
                    limit, _runActive ? "mid-run" : "hub");
                Recover("heartbeat-silent");
            }
        };
        _heartbeatWatch.Start();
    }

    private static void StopHeartbeatWatch()
    {
        try { _heartbeatWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _heartbeatWatch = null;
    }

    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Recover($"process-failed:{kind}");
    }

    /// <summary>Recovery ladder: relaunch once per session (mid-run state is lost - the
    /// sentinel records the abnormal end); a second failure gives up cleanly.</summary>
    private static void Recover(string reason)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            bool retry = !_relaunchedOnce;
            bool wasTest = _testMode;
            App.Logger?.Warning("DtrhHost: recovery ({Reason}) - {Action}", reason, retry ? "relaunching once" : "giving up");
            DisposeAll();
            if (retry)
            {
                _relaunchedOnce = true;
                Launch(wasTest);
            }
        });
    }

    // ============================ teardown ============================

    private static void ArmExitWatchdog()
    {
        CancelExitWatchdog();
        _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _exitWatchdog.Tick += (_, _) => DisposeAll();
        _exitWatchdog.Start();
    }

    private static void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _exitWatchdog = null;
    }

    private static void DisposeAll()
    {
        if (_disposing) return;   // _host.Dispose() closes the window, re-raising Closed -> here
        _disposing = true;

        // EMI Desk (MOMENTS `dtrhClosed`), the partner of the `dtrhOpened` fire in Launch. Here
        // rather than in CloseActive because this is the ONE funnel every exit reaches (graceful
        // close, watchdog, the window's own Closed event, app shutdown). No {n} xp: the run's own
        // total is reported to BarkService per run, not to the host, and nothing at teardown can
        // see it - the one line in the pool that asks for it is skipped by the engine.
        if (_emiOpenedUtc != DateTime.MinValue)
        {
            int emiMinutes = Math.Max(0, (int)(DateTime.UtcNow - _emiOpenedUtc).TotalMinutes);
            _emiOpenedUtc = DateTime.MinValue;
            try { App.EmiDesk?.Fire("dtrhClosed", new { minutes = emiMinutes }); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        try
        {
        CancelExitWatchdog();
        StopHeartbeatWatch();
        HookVideoEvents(false);
        // The window died with a descent still falling. Every exit the page can see books
        // itself first (the recap, the Escape hold, the host's end-run), so reaching here with
        // _runActive still true means the frame was closed out from under it - and an endless
        // descent has no other ending. Bank the last snapshot rather than dropping the run.
        // It is an ABANDONED booking like any other: paid pro rata for the seconds the snapshot
        // says were fallen, and counting as a run only past the minute mark.
        if (DtrhRunCloseRule.ShouldBookOnClose(_runActive, _lastRunProgress != null))
        {
            App.Logger?.Information("DtrhHost: the window closed mid-run - banking the last run snapshot");
            try
            {
                var snapshot = _lastRunProgress!;
                snapshot["abandoned"] = true;
                OnRunEnded(snapshot, fromTeardown: true);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
        // Never leave the toy running after the window dies (crash, watchdog, clean exit alike).
        try { Haptics.DtrhHapticDirector.OnClosed(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // Never leave a video or voiceline wedged paused if the window dies mid-freeze.
        if (_worldFrozen) { _worldFrozen = false; try { App.Video?.PlayPrimary(); } catch (Exception ex) { Diag.Swallowed(ex); } try { App.AvatarWindow?.ResumeSpokenAudio(); } catch (Exception ex) { Diag.Swallowed(ex); } }
        // ...and never leave every video silent because the dive ended while muted. Unconditional
        // (#1103): the _diveMuted flag and the VideoService's own mute could get out of step - a
        // mute applied while Application.Current was already gone set the flag and nothing else,
        // and this gate then skipped the only release there was, muting every video until restart.
        _diveMuted = false;
        try { App.Video?.SetExternalMute(false); } catch (Exception ex) { Diag.Swallowed(ex); }
        try { _meta?.FlushSave(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // The sentinel is NOT disarmed here any more. This teardown is where
        // Recover("process-failed") and Recover("heartbeat-silent") both land, and Recover's own
        // contract is "the sentinel records the abnormal end" - the old clear contradicted it and
        // reported a real WebView2 vanish as a clean run on the next launch. A deliberate close
        // mid-descent now costs one diagnostic line in the log, which is the cheaper mistake.
        _runActive = false;
        try { _host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _host = null;
        _meta = null;
        _exiting = false;
        // Bring the main CCP window back from the tray if we minimized it at launch.
        if (_minimizedMainWindow)
        {
            _minimizedMainWindow = false;
            try { (Application.Current?.MainWindow as MainWindow)?.ShowFromTray(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }
        App.Logger?.Information("DtrhHostService: closed");
        }
        finally { _disposing = false; }
    }

    private static int SafeMasterVolume()
    {
        try { return App.Settings?.Current?.MasterVolume ?? 100; }
        catch { return 100; }
    }

    /// <summary>Active persona/mod id for the page's VN portrait. Defaults to the
    /// Sissy set when no mod is resolvable (that's the only baked portrait set today).</summary>
    private static string SafeActiveModId()
    {
        try { return App.Mods?.ActiveModId ?? "builtin-sissyhypno"; }
        catch { return "builtin-sissyhypno"; }
    }
}
