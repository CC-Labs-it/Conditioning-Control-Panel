using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The For You feed's host (WPF Services/Fyp/FypHostService.cs): <c>fyp/index.html</c> on the app's origin,
    /// the library through the asset gate every game uses, the rules in Core <see cref="FypRules"/> (shared
    /// with WPF). BRIGHT LINE: batches and probes go straight to the provider, media only when RemoteAllowed.
    /// <para>ponytail: not on this head yet - eye control (init says off; a toggle gets eyeStatus "unsupported"),
    /// ghost mode (WPF's own unavailable answer), the file-menu, calibrate and EMI's fypOpened/fypClosed.</para>
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
                Post(FypRules.BuildInit(CoreSettings.Current, assets, eyeControl: false, FypStatsPath));
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
                case "settings-changed":
                    ApplyFypSetting((string?)o["key"], o["value"]);
                    return true;
                default:
                    return false;   // close / log / boot-error: the shell's; file-menu / calibrate: see ponytail
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
                    // WPF FailEyeControl (:1278): stored off again, and the page told the truth - the camera
                    // exists (WebcamTracker), this host just does not drive the feed from it yet.
                    s.FypEyeControl = false;
                    PostFypEyeStatus("unsupported");
                    break;
                case FypRules.SettingEffect.EyeControlOff:
                case FypRules.SettingEffect.EyeGazeChanged:
                    PostFypEyeStatus(null);
                    break;
            }
        }

        /// <summary>WPF PostEyeStatus: what is actually true, so the toggles render reality.</summary>
        private void PostFypEyeStatus(string? reason)
        {
            var s = CoreSettings.Current;
            Post(new { type = "eyeStatus", enabled = s.FypEyeControl, gaze = s.FypEyeGaze, running = false, calibrated = false, reason });
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
