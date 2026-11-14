using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF RemoteControlService.ExecuteCommand's dispatch table for the features Core drives. Runs on the
    /// UI thread (RemoteRelay marshals). Returns null when the verb ran, else the refusal the controller
    /// sees: a verb with no surface on this head says so rather than reporting "ok" and doing nothing.
    /// ponytail: wallpaper and the remaining session verbs have no Core entry point yet; they refuse until their services move.
    /// </summary>
    public static class RemoteCommands
    {
        public const string NotOnThisBuild = "not on this build";
        public const string AutonomyOff = "autonomy is off";

        /// <summary>The command log / notification label key per verb (WPF MainWindow.xaml.cs CommandLabels);
        /// anything else shows as the verb with spaces.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> LabelKeys = new()
        {
            ["show_pink_filter"] = "cmd_pink_filter_enabled", ["stop_pink_filter"] = "cmd_pink_filter_disabled",
            ["show_spiral"] = "cmd_spiral_enabled", ["stop_spiral"] = "cmd_spiral_disabled",
            ["start_bubbles"] = "cmd_bubbles_started", ["stop_bubbles"] = "cmd_bubbles_stopped",
            ["trigger_video"] = "cmd_video_triggered", ["trigger_haptic"] = "cmd_haptic_triggered",
            ["trigger_bubble_count"] = "cmd_bubble_count_triggered",
            ["start_autonomy"] = "cmd_autonomy_enabled", ["stop_autonomy"] = "cmd_autonomy_disabled",
            ["start_session"] = "cmd_session_started", ["pause_session"] = "cmd_session_paused",
            ["resume_session"] = "cmd_session_resumed", ["stop_session"] = "cmd_session_stopped",
            ["enable_strict_lock"] = "cmd_strict_lock_enabled", ["disable_strict_lock"] = "cmd_strict_lock_disabled",
            ["disable_panic"] = "cmd_panic_key_disabled", ["enable_panic"] = "cmd_panic_key_enabled",
            ["trigger_panic"] = "cmd_all_effects_stopped",
        };

        /// <summary>Verbs too frequent to log or announce (WPF MainWindow.xaml.cs SuppressedCommands).</summary>
        public static readonly System.Collections.Generic.HashSet<string> Quiet = new()
        { "trigger_flash", "trigger_subliminal", "set_pink_opacity", "set_spiral_opacity", "duck_audio", "unduck_audio",
          // v2: hold-to-buzz sends one every second while held (main 719ed9ca5)
          "haptic_level", "haptic_stop" };

        /// <summary>The remote haptic player (WPF RemoteControlService.RemoteHaptics), scaled by Easy.</summary>
        internal static Remote.CoreRemoteHapticDriver RemoteHaptics = new(() => EasyFactor);   // tests swap in a stepped clock

        /// <summary>WPF RemoteControlService.V2 EasyFactor: 1 per session, halved by each Easy press (floor 0.25);
        /// scales remote haptics and remote-set spiral / pink opacity.</summary>
        public static double EasyFactor { get; private set; } = 1.0;
        private static readonly Remote.EasedOpacity EasedSpiral = new(), EasedPink = new();

        /// <summary>WPF StopAllRemoteEffects -> StopSessionFromRemote (RemoteControlService.cs:1027): a forced stop
        /// (remote panic, the wearer's Stop signal) ends the running session. Core owns no session runner, so the
        /// shell sets this to its one MainShellWindow.StopSessionFromRemote.</summary>
        public static Action? StopSession;

        /// <summary>The head's embedded browser (WPF MainWindow.PlayHypnotubeFromRemote / StopBrowserVideoFromRemote,
        /// MainWindow.Browser.cs:3188 / :3228). Play returns null when the page was handed to the browser, else the
        /// refusal. Null = this head has no browser to put it in.</summary>
        public static Func<string, string?>? PlayHypnotube;
        public static Action? StopBrowserVideo;

        /// <summary>WPF ApplyEasy (RemoteControlService.V2.cs:87): a controller-set or showing spiral / pink
        /// fades, the remote haptic replays at the new level. UI thread.</summary>
        public static void ApplyEasy()
        {
            var next = Remote.RemoteEasy.Next(EasyFactor);
            if (next == EasyFactor) return;
            EasyFactor = next;
            Serilog.Log.Information("[RemoteControl] Easy: remote strength now x{Factor}", next);
            var s = CoreSettings.Current;
            var changed = false;
            if (EasedSpiral.Rescale(s.SpiralOpacity, s.SpiralEnabled, next) is int sv) { Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.SpiralOpacity, sv); s.SpiralOpacity = sv; changed = true; }
            if (EasedPink.Rescale(s.PinkFilterOpacity, s.PinkFilterEnabled, next) is int pv) { Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.PinkOpacity, pv); s.PinkFilterOpacity = pv; changed = true; }
            if (changed)
            {
                try { RefreshOverlays?.Invoke(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] Easy refresh failed"); }
                CoreSettings.Save();
            }
            RemoteHaptics.Rescale();
        }

        /// <summary>WPF ResetV2SessionState: session end, after the wearer's values were handed back.</summary>
        public static void ResetEasy()
        {
            EasedSpiral.Reset();
            EasedPink.Reset();
            EasyFactor = 1.0;
        }


        /// <summary>WPF EnsureOverlayRunning: a controller can show overlays with the engine stopped.
        /// The head refreshes its existing hosts; Core never owns a window.</summary>
        public static Action? RefreshOverlays;
        public static bool OverlayRunning { get; private set; }
        public static bool OverlaysStopped { get; private set; }
        private static int _overlayStopGeneration;
        public static int OverlayStopGeneration => Volatile.Read(ref _overlayStopGeneration);
        public static void ReleaseOverlayStop() => OverlaysStopped = false;

        // WPF RemoteControlService.cs:1204 starts the overlay when a controller joins.
        public static void EnsureOverlayRunning()
        {
            if (OverlayRunning && !OverlaysStopped) return;
            OverlayRunning = true;
            OverlaysStopped = false;
            try { RefreshOverlays?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay start failed"); }
        }

        public static void StopOverlays()
        {
            Interlocked.Increment(ref _overlayStopGeneration);
            if (!OverlayRunning && OverlaysStopped) return;
            OverlayRunning = false;
            OverlaysStopped = true;
            try { RefreshOverlays?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay stop failed"); }
        }

        private static string? OverlayCommand(string action, JObject? p)
        {
            // Do not promise a surface on heads that did not seed one.
            if (RefreshOverlays == null) return NotOnThisBuild;
            var s = CoreSettings.Current;
            switch (action)
            {
                case "show_pink_filter": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.Pink, 1); s.PinkFilterEnabled = true; break;
                case "stop_pink_filter": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.Pink, 0); s.PinkFilterEnabled = false; break;
                case "show_spiral": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.Spiral, 1); s.SpiralEnabled = true; break;
                case "stop_spiral": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.Spiral, 0); s.SpiralEnabled = false; break;
                case "set_pink_opacity":
                    if (p == null) return null; // WPF null params is a no-op
                    var pink = EasedPink.Ask(p["value"]?.Value<int>() ?? 25, 50, EasyFactor);
                    Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.PinkOpacity, pink);
                    s.PinkFilterOpacity = pink; break;
                case "set_spiral_opacity":
                    if (p == null) return null;
                    var spiral = EasedSpiral.Ask(p["value"]?.Value<int>() ?? 25, 100, EasyFactor);
                    Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.SpiralOpacity, spiral);
                    s.SpiralOpacity = spiral; break;
            }
            OverlayRunning = true;
            OverlaysStopped = false;
            RefreshOverlays();
            CoreSettings.Save();
            return null;
        }

        /// <summary>WPF App.Autonomy: the head owns the scheduler. Starts (true) or stops (false) the runtime,
        /// never the saved switch; false back means nothing ran. Unseeded: the verbs refuse.</summary>
        public static Func<bool, bool>? AutonomyCommand;
        /// <summary>WPF AutonomyService.CancelActivePulses.</summary>
        public static Action? CancelAutonomyPulses;
        /// <summary>WPF App.Autonomy.IsEnabled, for the status's "autonomy" active service.</summary>
        public static Func<bool>? AutonomyRunning;

        /// <summary>WPF RemoteEndRule.LeavesLocalRun (RemoteControlService.cs:964, :1154): a leave or session end
        /// over the wearer's own run hands back only what the controller holds. Remote session verbs are not
        /// ported, so a running engine is always the wearer's own. Only a session end leaves it: a panic and a
        /// leash cut keep the full teardown (RemoteEndRule.cs:26-27, EndSessionNow :344).</summary>
        internal static bool LeavesLocalRun(bool force, bool leashCut = false) => !force && !leashCut && CoreEngine.IsRunning;

        /// <summary>WPF StopAllRemoteEffects (panic, session end; RemoteControlService.cs:980): pulses go and the
        /// runtime stops, then comes back when the wearer's own switch is on (#299). A controller leaving
        /// only cancels pulses (StopRemoteTriggeredEffects, :1159).</summary>
        internal static void ResetAutonomy()
        {
            try
            {
                CancelAutonomyPulses?.Invoke();
                if (AutonomyCommand?.Invoke(false) == true && CoreSettings.Current.AutonomyModeEnabled) AutonomyCommand(true);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] autonomy reset failed"); }
        }

        private static int _panicGeneration;
        /// <summary>Moves on every panic: a command fetched before it never runs after it (RemoteRelay).</summary>
        public static int PanicGeneration => Volatile.Read(ref _panicGeneration);

        /// <summary>Panic stops the remote haptic loop (decisions 2026-10-08). HapticMixer.PanicStop only
        /// silences for 400 ms; without this the driver's next tick restarts the loop. Every panic path
        /// on every head calls it, any thread.</summary>
        public static void StopHaptics()
        {
            Interlocked.Increment(ref _panicGeneration);
            try { RemoteHaptics.Stop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] remote haptic stop failed"); }
        }

        public static string? Execute(string action, JObject? p)
        {
            var s = CoreSettings.Current;
            switch (action)
            {
                case "show_pink_filter": case "stop_pink_filter":
                case "show_spiral": case "stop_spiral":
                case "set_pink_opacity": case "set_spiral_opacity":
                    return OverlayCommand(action, p);
                case "trigger_flash":
                    if (CoreFlash.ShowProvider is not { } flash) return NotOnThisBuild;
                    flash();
                    return null;
                case "trigger_subliminal":
                    var text = CoreSubliminal.PickPhrase();
                    if (string.IsNullOrWhiteSpace(text)) return "no phrases";
                    if (CoreSubliminal.ShowProvider is not { } sub) return NotOnThisBuild;
                    sub(text);
                    return null;
                case "trigger_custom_subliminal":
                    var customText = p?["text"]?.ToString();
                    if (string.IsNullOrWhiteSpace(customText)) return null;   // WPF: blank text is a no-op
                    if (CoreSubliminal.CustomShowProvider is not { } custom) return NotOnThisBuild;
                    custom(customText);
                    return null;
                case "start_brain_drain":
                case "stop_brain_drain":
                    var on = action == "start_brain_drain";
                    if (on && CoreEngine.StartBrainDrain == null) return NotOnThisBuild;
                    Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.BrainDrain, on ? 1 : 0);
                    s.BrainDrainEnabled = on;
                    if (on) CoreEngine.StartBrainDrain?.Invoke(); else CoreEngine.StopBrainDrain?.Invoke();
                    RefreshOverlays?.Invoke();
                    CoreSettings.Save();
                    return null;
                case "start_flash": s.FlashEnabled = true; CoreFlash.Start(); return null;
                case "stop_flash": CoreFlash.Stop(); return null;
                case "start_subliminal": s.SubliminalEnabled = true; CoreSubliminal.Start(); return null;
                case "stop_subliminal": CoreSubliminal.Stop(); return null;
                case "start_bubbles": CoreBubbles.Start(); return null;
                case "stop_bubbles": CoreBubbles.Stop(); return null;
                case "start_bounce_text": CoreBouncingText.Start(); return null;
                case "stop_bounce_text": CoreBouncingText.Stop(); return null;
                // WPF RemoteControlService.cs:1413: the URL is the only thing crossing the trust boundary, so
                // only a real hypnotube.com video page reaches the browser. WPF drops a bad one silently;
                // here it is reported, as every other refusal (ccp-bugs#1138).
                case "play_hypnotube":
                    var htUrl = p?["url"]?.ToString();
                    if (!Helpers.HtUrlHelper.IsEligibleHtUrl(htUrl)) return Localization.Loc.Get("remote_invalid_hypnotube_url");
                    return PlayHypnotube is { } play ? play(htUrl!) : NotOnThisBuild;
                case "trigger_video":
                    if (CoreEngine.Video is not { } video) return NotOnThisBuild;
                    // WPF RemoteControlService.cs:1395 acknowledges every trigger (held behind the feed, empty
                    // library): only a video that is really playing is refused (#3776).
                    return video.Trigger() || !video.IsPlaying ? null : "a video is already playing";
                case "start_video": if (CoreEngine.Video == null) return NotOnThisBuild; CoreEngine.Video.Start(); return null;
                case "stop_video": CoreEngine.Video?.Stop(); StopBrowserVideo?.Invoke(); return null;
                case "trigger_bubble_count": if (CoreEngine.BubbleCount == null) return NotOnThisBuild; CoreEngine.BubbleCount.Trigger(forceTest: true); return null;
                case "trigger_mind_wipe":
                    if (CoreMindWipe.TriggerOnceProvider == null) return NotOnThisBuild;
                    if (CoreMindWipe.ClipCount > 0) CoreMindWipe.TriggerOnce();
                    return null;
                case "start_mind_wipe":
                    if (CoreMindWipe.StartProvider == null) return NotOnThisBuild;
                    CoreMindWipe.Start(s.MindWipeFrequency, s.MindWipeVolume / 100.0);
                    return null;
                case "stop_mind_wipe": CoreMindWipe.Stop(); return null;
                case "trigger_lock_card": if (CoreLockCard.ShowHandler == null) return NotOnThisBuild; CoreLockCard.Show(); return null;
                case "start_lock_card": s.LockCardEnabled = true; LockCardScheduler.Instance.Start(); return null;
                case "stop_lock_card": LockCardScheduler.Instance.Stop(); return null;
                case "start_autonomy":
                case "stop_autonomy":
                    if (AutonomyCommand == null) return NotOnThisBuild;
                    return AutonomyCommand(action == "start_autonomy") ? null : AutonomyOff;   // switch, consent or entitlement refused
                case "trigger_haptic":
                    if (CoreHaptics.Service?.IsConnected != true) return "no_device";   // WPF 7b22ece8c (ccp-bugs #1065): never a silent "ok"
                    _ = CoreHaptics.Service!.TriggerAsync("remote_control", 0.7 * EasyFactor, 2000); return null;
                // Remote Control v2 (main 719ed9ca5): a pattern or hold-to-buzz level replaces whatever remote haptic plays.
                case "haptic_pattern":
                case "haptic_level":
                    var plan = action == "haptic_level" ? Remote.RemoteHapticPlan.FromLevel(p, out var why) : Remote.RemoteHapticPlan.FromPattern(p, out why);
                    if (plan == null) return why ?? "bad params";
                    RemoteHaptics.Play(plan);
                    return null;
                case "haptic_stop": RemoteHaptics.Stop(); return null;
                case "duck_audio": CoreAudio.Duck(80); return null;
                case "unduck_audio": CoreAudio.Unduck(); return null;
                // Noted before it lands: every end path hands the wearer's own value back (RemoteWearer).
                case "enable_strict_lock": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.StrictLock, 1); s.StrictLockEnabled = true; CoreSettings.Save(); return null;
                case "disable_strict_lock": Remote.RemoteWearer.NoteWrite(Remote.RemoteHeld.StrictLock, 0); s.StrictLockEnabled = false; CoreSettings.Save(); return null;
                case "enable_panic": s.PanicKeyEnabled = true; CoreSettings.Save(); SyncPanicUi(); return null;
                case "trigger_panic": StopEffects(force: true); return null;
                default: return NotOnThisBuild;
            }
        }

        /// <summary>WPF StopAllRemoteEffects: everything a controller could start stops and the wearer's own
        /// values come back (RemoteWearer: their strict lock, panic on unless a running Lockdown holds it off).
        /// Only <paramref name="force"/> (trigger_panic) takes the subject's own engine down (#878).
        /// <paramref name="leashCut"/> keeps the full teardown over a local run (WPF RemoteEndRule.cs:26-27).</summary>
        public static void StopEffects(bool force, bool leashCut = false)
        {
            // A leave and the session end both arrive here unforced; RemoteRelay.Cleanup adds the session-end reset.
            if (LeavesLocalRun(force, leashCut))
            {
                // WPF ReleaseRemoteHoldings (RemoteControlService.cs:964, :1125, :1154): the wearer's run keeps its
                // services, autonomy and cards; only what the controller holds goes back.
                RemoteHaptics.Stop();
                var handedBack = Remote.RemoteWearer.HandBackAllButStrict();
                Remote.RemoteWearer.ReleaseSafetyState();
                // WPF ReleaseRemoteHoldings ends with App.Overlay.RefreshOverlays(): the wearer's own pink/spiral repaint.
                if (handedBack > 0)
                    try { RefreshOverlays?.Invoke(); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay restore refresh failed"); }
                return;
            }
            if (force) ResetAutonomy();
            else try { CancelAutonomyPulses?.Invoke(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] autonomy pulse cancel failed"); }
            if (force) Commands.AiCommandService.CancelAll();   // a remote panic cancels pending AI follow-ups too
            // A controller's panic is a way out like the key: Circe's Tab opens its safety hold (PanicChasterHold).
            if (force) Safety.PanicChasterHold.Open("remote panic");
            // WPF RemoteControlService.StopAllRemoteEffects: the controller's panic arms the post-panic quiet too
            // (PanicAftermath). It only ever refuses; no pause mark, so the controller may still resume.
            if (force) Safety.PanicAftermath.ArmQuiet();
            if (force) StopHaptics(); else RemoteHaptics.Stop();
            try { CoreHaptics.Service?.PanicStop(); } catch { }
            CoreAudio.Unduck();
            // WPF StopAllRemoteEffects (:1001, :1164): the controller's browser video stops too; a no-op unless one plays.
            try { StopBrowserVideo?.Invoke(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] browser video stop failed"); }
            // Keep the base's wearer restoration; never clear the subject's saved switches.
            if (force || !CoreEngine.IsRunning) StopOverlays();
            var playerStopped = force || !CoreEngine.IsRunning;
            if (force)
            {
                // Session, then engine, as WPF.
                try { StopSession?.Invoke(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] session stop failed"); }
                CoreEngine.Stop();
            }
            else
            {
                CoreFlash.Stop(); CoreSubliminal.Stop(); CoreBubbles.Stop(); CoreBouncingText.Stop();
                CoreEngine.Video?.Stop(); CoreEngine.BubbleCount?.Stop(); LockCardScheduler.Instance.Stop(); CoreMindWipe.Stop();
                if (playerStopped)
                    try { CoreEngine.StopBrainDrain?.Invoke(); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] Brain Drain stop failed"); }
            }
            CoreLockCard.CloseAll();   // WPF LockCardWindow.ForceCloseAll (:1020, :1179), a card still posted included
            // WPF (main 4adc4c476, 2a712d9da): never a blanket strict-off. The strict lock goes back to the
            // wearer's own (a running Lockdown keeps the one it forced, its record corrected), the panic key
            // comes back on except while a running Lockdown holds it off; its own end restores it.
            var restoredVisuals = Remote.RemoteWearer.HandBackAllButStrict(playerStopped);
            Remote.RemoteWearer.ReleaseSafetyState(); // WPF RemoteControlService.cs:1036-1043: safety before head refresh
            if (restoredVisuals > 0)
                try { RefreshOverlays?.Invoke(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay restore refresh failed"); }
        }

        private static void SyncPanicUi() { try { LockdownService.PanicKeyUiSync?.Invoke(); } catch { } }
    }
}
