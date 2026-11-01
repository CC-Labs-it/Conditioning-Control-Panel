using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The For You feed's host (WPF Services/Fyp/FypHostService.cs): <c>fyp/index.html</c> on the app's origin,
    /// the library through the asset gate every game uses, the rules in Core <see cref="FypRules"/> (shared
    /// with WPF). BRIGHT LINE: batches and probes go straight to the provider, media only when RemoteAllowed.
    /// Ghost mode and EMI moments remain unported.
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string FypId = "fyp";

        // WPF FeedOwnsTheScreen. Ghost mode is unavailable on this head, so every visible feed counts.
        internal static bool IsForYouOnScreen()
        {
            lock (Open) return Open.Any(w => w.Spec.Id == FypId && w.IsVisible);
        }

        private FypMetaStore? _fypMeta;
        private bool _fypHooked;
        private bool _fypFileMenuHooked;
        internal ContextMenu? FypFileMenu { get; private set; }
        private int _fypRemoteInFlight;   // 0/1 via Interlocked
        private readonly HashSet<string> _fypProbesInFlight = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Test seams: the remote batch and the sub probe (default: the Core coordinator).</summary>
        internal Func<CancellationToken, Task<FypOnlineCoordinator.FeedBatch>>? FypFetchOverride;
        internal Func<string, CancellationToken, Task<SubProbe>>? FypProbeOverride;

        /// <summary>WPF FypHostService StatsFilePath: fyp_stats.json under the user data folder.</summary>
        internal static string FypStatsPath => Path.Combine(CorePaths.UserData, "fyp_stats.json");

        /// <summary>WPF OpenFypFeed (MainWindow.Lab.cs:246): premium at the door, refused out loud, free day by key.</summary>
        private static bool FypGate()
        {
            if (!TierGate.DemandPremium(Loc.Get("tab_fyp"), "fyp")) return false;
            EmiState.NoteUsage(FypId);   // WPF Launch :78 NoteOpen("fyp"): the ring learns from every open
            return true;
        }

        /// <summary>WPF PostInit (:162): one init payload on page-ready.</summary>
        private void PostFypInit()
        {
            try
            {
                _fypMeta ??= new FypMetaStore();
                if (!_fypHooked)
                {
                    _fypHooked = true;
                    // WPF Close (:132): the probes and every channel's rotation are kept.
                    Closed += (_, _) =>
                    {
                        try { _fypMeta?.Save(); } catch (Exception ex) { Log.Debug("[Game] fyp meta save: {E}", ex.Message); }
                        try { FypOnlineCoordinator.SaveAll(); } catch (Exception ex) { Log.Debug("[Game] fyp channels save: {E}", ex.Message); }
                    };
                }
                var assets = FypAssets();
                Post(FypRules.BuildInit(CoreSettings.Current, assets, eyeControl: true, FypStatsPath));
                if (CoreSettings.Current.FypEyeControl) StartFypEyeControl();
                Log.Information("[Game] fyp: page ready, init sent ({N} library clips)", assets.Count);   // WPF :124 "launched"
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: init failed: {E}", ex.Message); }
        }

        /// <summary>The library manifest with each url on this window's asset gate (WPF's https://ccp.assets/).</summary>
        private List<FypAssetManifest.Entry> FypAssets()
        {
            var list = FypAssetManifest.Build(_fypMeta ??= new FypMetaStore());
            var server = _server;
            if (server == null) return list;
            return list.Select(e => new FypAssetManifest.Entry
            {
                Id = e.Id, Url = server.AssetUrl(e.Id), Type = e.Type, Filename = e.Filename, Folder = e.Folder,
                DurationMs = e.DurationMs, Width = e.Width, Height = e.Height,
                Origin = e.Origin, PosterUrl = e.PosterUrl, SmallUrl = e.SmallUrl,
            }).ToList();
        }

        /// <summary>WPF FypHostService.OnPageMessage (:188). True = claimed.</summary>
        private bool HandleFyp(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    PostFypInit();
                    return true;
                case "stats-save":
                    if (o["stats"] is JObject stats) FypRules.SaveStats(FypStatsPath, stats);
                    return true;
                case "asset-meta":
                {
                    // Remote pools churn forever: never let their probes grow fyp_meta.json.
                    var id = (string?)o["id"];
                    if (!FypRules.IsRemoteId(id))
                        _fypMeta?.Update(id, (long?)o["durationMs"], (int?)o["width"], (int?)o["height"]);
                    return true;
                }
                case "media-error":
                {
                    // Warning so it lands in bug reports (the "black tiles" class, #562).
                    var id = (string?)o["id"];
                    Log.Warning("[FYP] media-error for {Id}: code={Code} {Message}", id, (int?)o["code"] ?? 0, (string?)o["message"] ?? "");
                    if (!FypRules.IsRemoteId(id)) _fypMeta?.RecordFailure(id);
                    return true;
                }
                case "clip-viewed":
                {
                    // Remote clips feed the channel-taste average before the XP cap gets a say.
                    var segId = (string?)o["segId"];
                    if (FypRules.IsRemoteId(segId))
                    {
                        try { FypOnlineCoordinator.Fyp.RecordDwell(segId!, (long?)o["dwellMs"] ?? 0); }
                        catch (Exception ex) { Log.Debug("[Game] fyp: dwell record failed: {E}", ex.Message); }
                    }
                    if (FypRules.AllowClipXp()) CoreProgression.AddXP(FypRules.ClipXp, "Fyp");
                    return true;
                }
                case "attention-hit":
                    CoreProgression.AddXP(FypRules.AttentionXp, "Fyp");
                    return true;
                case "need-remote":
                    _ = ServeFypRemoteBatch();
                    return true;
                case "library-remove":
                    FypRules.RemoveLibrarySub(CoreSettings.Current, (string?)o["name"] ?? (string?)o["sub"]);
                    Post(FypRules.LibraryFrame(CoreSettings.Current));
                    return true;
                case "probe-sub":
                    FypLastProbe = ProbeFypSub((string?)o["sub"]);
                    return true;
                case "file-menu":
                    ShowFypFileMenu((string?)o["id"]);
                    return true;
                case "calibrate":
                    FypCalibrationTask = CalibrateFypGaze();
                    return true;
                case "settings-changed":
                    ApplyFypSetting((string?)o["key"], o["value"]);
                    return true;
                default:
                    return false;   // close / log / boot-error: the shell's
            }
        }

        /// <summary>WPF FypFileMenu.Show: pointer menu for a served, existing library clip only.</summary>
        private void ShowFypFileMenu(string? id)
        {
            FypFileMenu?.Close();
            FypFileMenu = null;
            if (_closing) return;
            var path = FypFileRules.ResolveLocal(CorePaths.EffectiveAssets, id);
            if (path == null || !File.Exists(path)) return;
            var open = new MenuItem { Header = Loc.Get("fyp_menu_open_file") };
            open.Click += (_, _) => { if (File.Exists(path)) ExternalOpener.Open(path); };
            var reveal = new MenuItem { Header = Loc.Get("fyp_menu_show_in_folder") };
            // WPF FypFileMenu.cs:28: select the file, or open its folder if it has gone.
            reveal.Click += (_, _) => ExternalOpener.Reveal(path);
            var menu = new ContextMenu { Placement = PlacementMode.Pointer, ItemsSource = new[] { open, reveal } };
            FypFileMenu = menu;
            menu.Open(Web);
            // The popup cannot outlive its feed, including the existing games panic surface.
            if (!_fypFileMenuHooked)
            {
                _fypFileMenuHooked = true;
                Closed += (_, _) => { FypFileMenu?.Close(); FypFileMenu = null; };
            }
        }

        private void ApplyFypSetting(string? key, JToken? value)
        {
            var s = CoreSettings.Current;
            switch (FypRules.ApplySetting(s, key, value))
            {
                case FypRules.SettingEffect.GhostOn:
                    // WPF EnterGhost's refusal (:864-866): the toggle snaps back off, then the page says why.
                    Post(new { type = "clickThrough", on = false });
                    Post(new { type = "ghost-unavailable", reason = "unsupported" });
                    break;
                case FypRules.SettingEffect.EyeControlOn:
                    StartFypEyeControl();
                    break;
                case FypRules.SettingEffect.EyeControlOff:
                    DisableFypEyeControl();
                    PostFypEyeStatus(null);
                    break;
                case FypRules.SettingEffect.EyeGazeChanged:
                    SyncFypGaze();
                    PostFypEyeStatus(null);
                    break;
            }
        }

        // WPF FypHostService.cs:924-1045: consent, off-thread start, and camera ownership.
        private bool _fypEyeSubscribed, _fypGazeSubscribed, _fypStartedCamera, _fypEyeEnabling;
        // WPF's singleton host shares an in-flight enable across close and reopen (:926-969).
        private static Task<bool>? _fypCameraStart;
        private static GameWindow? _fypCameraClaim;
        private static readonly object FypStopGate = new();
        private static Task _fypCameraStopTask = Task.CompletedTask;
        internal static Task FypCameraStopTask { get { lock (FypStopGate) return _fypCameraStopTask; } }
        // Panic can queue a second tracker stop after games close; wait for both before reopening.
        internal static void TrackFypCameraStop(Task stop) { lock (FypStopGate) _fypCameraStopTask = Task.WhenAll(_fypCameraStopTask, stop); }
        private int _fypEyeGeneration;
        private DateTime _fypLastGaze = DateTime.MinValue;
        internal Task? FypEyeTask { get; private set; }
        internal Task? FypCalibrationTask { get; private set; }
        // Test seams keep camera hardware and modal dialogs out of the headless suite.
        internal Func<Task<bool>>? FypConsentOverride, FypStartCameraOverride;
        internal Func<bool>? FypCameraRunningOverride;
        internal Action? FypStopCameraOverride;
        internal Func<Task>? FypCalibrationOverride;
        private bool FypCameraRunning => FypCameraRunningOverride?.Invoke() ?? WebcamTracker.Instance.IsRunning;

        private void StartFypEyeControl()
        {
            if (_closing || _fypEyeEnabling) return;
            if (_fypEyeSubscribed) { SyncFypGaze(); PostFypEyeStatus(null); return; }
            FypEyeTask = EnableFypEyeControl();
        }

        private async Task EnableFypEyeControl()
        {
            _fypEyeEnabling = true;
            int generation = _fypEyeGeneration;
            try
            {
                await Task.Yield(); // never show a modal dialog inside the web message callback
                if (!FypEyeWanted(generation)) return;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    bool consent;
                    if (FypConsentOverride != null) consent = await FypConsentOverride();
                    else
                    {
                        var dialog = new WebcamConsentDialog();
                        var accepted = await dialog.ShowDialogSafe<bool?>(this);
                        consent = accepted == true && dialog.ConsentGiven;
                    }
                    if (!FypEyeWanted(generation)) return;
                    if (!consent) { FailFypEyeControl("consent"); return; }
                }
                // Do not start a new session until an earlier FYP-owned stop has finished.
                Task stopping;
                do { stopping = FypCameraStopTask; await stopping; }
                while (!ReferenceEquals(stopping, FypCameraStopTask));
                if (!FypEyeWanted(generation)) return;
                if (_fypCameraStart != null || !FypCameraRunning)
                {
                    PostFypEyeStatus("starting");
                    var start = _fypCameraStart ??= FypStartCameraOverride?.Invoke() ?? WebcamTracker.Instance.StartAsync();
                    _fypCameraClaim = this;
                    bool started;
                    try { started = await start; }
                    catch { if (ReferenceEquals(_fypCameraStart, start)) _fypCameraStart = null; throw; }
                    if (!started)
                    {
                        if (ReferenceEquals(_fypCameraStart, start)) _fypCameraStart = null;
                        if (FypEyeWanted(generation)) FailFypEyeControl(
                            WebcamTracker.Instance.State == WebcamTrackingState.Error ? "error" : "no-camera");
                        return;
                    }
                    if (ReferenceEquals(_fypCameraClaim, this) && FypEyeWanted(generation))
                    {
                        _fypCameraClaim = null;
                        if (ReferenceEquals(_fypCameraStart, start)) _fypCameraStart = null;
                        _fypStartedCamera = true;
                    }
                    else if (_fypCameraClaim == null && ReferenceEquals(_fypCameraStart, start))
                    {
                        _fypCameraStart = null;
                        _fypStartedCamera = true; // nobody adopted the late start
                        DisableFypEyeControl();
                        return;
                    }
                }
                if (!FypEyeWanted(generation)) { DisableFypEyeControl(); return; }
                var tracker = WebcamTracker.Instance;
                tracker.OnBlink += OnFypBlink;
                tracker.OnEyesClosedLong += OnFypEyesClosed;
                _fypEyeSubscribed = true;
                SyncFypGaze();
                PostFypEyeStatus(null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Game] fyp: eye control failed");
                if (FypEyeWanted(generation)) FailFypEyeControl("error");
                else DisableFypEyeControl();
            }
            finally
            {
                _fypEyeEnabling = false;
                // An off/on while a dialog or camera start was pending needs a fresh attempt.
                if (generation != _fypEyeGeneration && !_closing && CoreSettings.Current.FypEyeControl)
                    StartFypEyeControl();
            }
        }

        private bool FypEyeWanted(int generation) => !_closing && generation == _fypEyeGeneration && CoreSettings.Current.FypEyeControl;

        private void FailFypEyeControl(string reason)
        {
            CoreSettings.Current.FypEyeControl = false;
            DisableFypEyeControl();
            PostFypEyeStatus(reason);
        }

        private void DisableFypEyeControl()
        {
            _fypEyeGeneration++;
            if (ReferenceEquals(_fypCameraClaim, this)) _fypCameraClaim = null;
            var tracker = WebcamTracker.Instance;
            if (_fypEyeSubscribed) { tracker.OnBlink -= OnFypBlink; tracker.OnEyesClosedLong -= OnFypEyesClosed; }
            if (_fypGazeSubscribed) tracker.OnGazeMove -= OnFypGaze;
            _fypEyeSubscribed = _fypGazeSubscribed = false;
            if (_fypStartedCamera)
            {
                _fypStartedCamera = false;
                // StopAsync invalidates pending starts before hopping off the UI thread.
                TrackFypCameraStop(FypStopCameraOverride != null ? Task.Run(FypStopCameraOverride) : tracker.StopAsync());
            }
        }

        private void SyncFypGaze()
        {
            var tracker = WebcamTracker.Instance;
            bool want = _fypEyeSubscribed && CoreSettings.Current.FypEyeControl && CoreSettings.Current.FypEyeGaze && tracker.Calibration != null;
            if (want == _fypGazeSubscribed) return;
            if (want) tracker.OnGazeMove += OnFypGaze; else tracker.OnGazeMove -= OnFypGaze;
            _fypGazeSubscribed = want;
        }

        // WPF :1050-1085: status describes the tracker and calibration, including after the dialog.
        private void PostFypEyeStatus(string? reason)
        {
            var s = CoreSettings.Current;
            Post(new { type = "eyeStatus", enabled = s.FypEyeControl, gaze = s.FypEyeGaze,
                running = FypCameraRunning, calibrated = WebcamTracker.Instance.Calibration != null, reason });
        }

        private async Task CalibrateFypGaze()
        {
            if (WebcamCalibrationWindow.IsShowing) return;
            if (!FypCameraRunning) { PostFypEyeStatus(null); return; }
            try
            {
                if (FypCalibrationOverride != null) await FypCalibrationOverride();
                else await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                if (_closing) return;
                SyncFypGaze();
                PostFypEyeStatus(null);
            }
            catch (Exception ex) { Log.Warning(ex, "[Game] fyp: gaze calibration failed"); }
        }

        // WPF :1090-1150: tracker events arrive on the UI thread; gaze is capped at 10 Hz.
        private void OnFypBlink() { try { Post(new { type = "blink" }); } catch (Exception ex) { Log.Debug("[Game] fyp: blink post: {E}", ex.Message); } }
        private void OnFypEyesClosed() { try { Post(new { type = "eyesClosed" }); } catch (Exception ex) { Log.Debug("[Game] fyp: eyesClosed post: {E}", ex.Message); } }
        private void OnFypGaze(Point p)
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _fypLastGaze).TotalMilliseconds < 100 || Web.Bounds.Width <= 0 || Web.Bounds.Height <= 0) return;
                var bounds = WebcamTracker.Instance.Calibration?.MonitorBounds;
                double calScale = bounds is { DpiScale: > 0.25 and < 8.0 } ? bounds.DpiScale : 1.0;
                double originX = bounds?.DeviceName != null ? bounds.X : 0;
                double originY = bounds?.DeviceName != null ? bounds.Y : 0;
                var webOrigin = Web.PointToScreen(new Point(0, 0));
                double scale = TopLevel.GetTopLevel(Web)?.RenderScaling ?? 1.0;
                double x = (p.X * calScale + originX - webOrigin.X) / scale / Web.Bounds.Width;
                double y = (p.Y * calScale + originY - webOrigin.Y) / scale / Web.Bounds.Height;
                bool inside = x >= 0 && x <= 1 && y >= 0 && y <= 1;
                _fypLastGaze = now;
                Post(new { type = "gaze", x = Math.Clamp(x, 0, 1), y = Math.Clamp(y, 0, 1), inside });
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: gaze post: {E}", ex.Message); }
        }

        /// <summary>WPF ServeRemoteBatch (:360): one batch per ask, single-flight, refused quietly without RemoteAllowed.</summary>
        internal async Task ServeFypRemoteBatch()
        {
            var s = CoreSettings.Current;
            if (!FypRules.RemoteAllowed(s))
            {
                Log.Debug("[Game] fyp: need-remote refused (source {Fyp}/{App}, consent {Consent})", s.FypSource, s.MediaSource, s.HasRemoteMediaConsent);
                return;
            }
            if (Interlocked.CompareExchange(ref _fypRemoteInFlight, 1, 0) != 0) return;
            try
            {
                var fetch = FypFetchOverride ?? (ct => FypOnlineCoordinator.Fyp.FetchBatchDetailedAsync(ct));
                var batch = await Task.Run(() => fetch(CancellationToken.None)).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closing) return;   // feed closed while fetching
                    foreach (var frame in FypRules.BatchFrames(batch)) Post(frame);
                });
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: remote batch failed: {E}", ex.Message); }
            finally { Interlocked.Exchange(ref _fypRemoteInFlight, 0); }
        }

        /// <summary>The last probe started (tests await it).</summary>
        internal Task? FypLastProbe { get; private set; }

        /// <summary>WPF ProbeCustomSub (:577): one upstream request, committed here, never by the page. No consent
        /// gate, as WPF: only the media fetch needs one.</summary>
        internal async Task ProbeFypSub(string? rawSub)
        {
            var clean = FypOnlineCoordinator.SanitizeSub(rawSub);
            if (clean == null) { Post(FypRules.InvalidProbeFrame(rawSub)); return; }
            lock (_fypProbesInFlight) { if (!_fypProbesInFlight.Add(clean)) return; }
            try
            {
                var run = FypProbeOverride ?? ((sub, ct) => FypOnlineCoordinator.ProbeSubAsync(sub, ct));
                var probe = await Task.Run(() => run(clean, CancellationToken.None)).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closing) return;   // feed closed while probing
                    Post(FypRules.CommitProbe(CoreSettings.Current, clean, probe));
                    // The pill row is built from the library, so it has to hear about a new keeper.
                    if (probe.Ok) Post(FypRules.LibraryFrame(CoreSettings.Current));
                });
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: sub probe failed: {E}", ex.Message); }
            finally { lock (_fypProbesInFlight) _fypProbesInFlight.Remove(clean); }
        }
    }
}
