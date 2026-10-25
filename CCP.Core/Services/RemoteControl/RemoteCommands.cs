using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF RemoteControlService.ExecuteCommand's dispatch table for the features Core drives. Runs on the
    /// UI thread (RemoteRelay marshals). Returns null when the verb ran, else the refusal the controller
    /// sees: a verb with no surface on this head says so rather than reporting "ok" and doing nothing.
    /// ponytail: HypnoTube, autonomy, Melt (start/stop_brain_drain),
    /// wallpaper and the session verbs have no Core entry point yet; they refuse until their services move.
    /// </summary>
    public static class RemoteCommands
    {
        public const string NotOnThisBuild = "not on this build";

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

        /// <summary>The remote haptic player (WPF RemoteControlService.RemoteHaptics). Easy is not on this head
        /// (no HUD), so the scale stays 1.</summary>
        internal static Remote.CoreRemoteHapticDriver RemoteHaptics = new(() => 1.0);   // tests swap in a stepped clock

        /// <summary>WPF EnsureOverlayRunning: a controller can show overlays with the engine stopped.
        /// The head refreshes its existing hosts; Core never owns a window.</summary>
        public static Action? RefreshOverlays;
        public static bool OverlayRunning { get; private set; }
        public static bool OverlaysStopped { get; private set; }
        public static void ReleaseOverlayStop() => OverlaysStopped = false;

        public static void StopOverlays()
        {
            if (!OverlayRunning && !OverlaysStopped) return;
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
                case "show_pink_filter": s.PinkFilterEnabled = true; break;
                case "stop_pink_filter": s.PinkFilterEnabled = false; break;
                case "show_spiral": s.SpiralEnabled = true; break;
                case "stop_spiral": s.SpiralEnabled = false; break;
                case "set_pink_opacity":
                    if (p == null) return null; // WPF null params is a no-op
                    s.PinkFilterOpacity = Math.Clamp(p["value"]?.Value<int>() ?? 25, 0, 50); break;
                case "set_spiral_opacity":
                    if (p == null) return null;
                    s.SpiralOpacity = Math.Clamp(p["value"]?.Value<int>() ?? 25, 0, 100); break;
            }
            OverlayRunning = true;
            OverlaysStopped = false;
            RefreshOverlays();
            CoreSettings.Save();
            return null;
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
                case "trigger_custom_subliminal":
                    var text = action == "trigger_subliminal" ? CoreSubliminal.PickPhrase() : p?["text"]?.ToString();
                    if (string.IsNullOrWhiteSpace(text)) return action == "trigger_subliminal" ? "no phrases" : null;   // WPF: blank text is a no-op
                    if (CoreSubliminal.ShowProvider is not { } sub) return NotOnThisBuild;
                    sub(text);
                    return null;
                case "start_flash": s.FlashEnabled = true; CoreFlash.Start(); return null;
                case "stop_flash": CoreFlash.Stop(); return null;
                case "start_subliminal": s.SubliminalEnabled = true; CoreSubliminal.Start(); return null;
                case "stop_subliminal": CoreSubliminal.Stop(); return null;
                case "start_bubbles": CoreBubbles.Start(); return null;
                case "stop_bubbles": CoreBubbles.Stop(); return null;
                case "start_bounce_text": CoreBouncingText.Start(); return null;
                case "stop_bounce_text": CoreBouncingText.Stop(); return null;
                case "trigger_video": return CoreEngine.Video?.Trigger() == true ? null : CoreEngine.Video == null ? NotOnThisBuild : "a video is already playing";
                case "start_video": if (CoreEngine.Video == null) return NotOnThisBuild; CoreEngine.Video.Start(); return null;
                case "stop_video": CoreEngine.Video?.Stop(); return null;
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
                case "start_lock_card": s.LockCardEnabled = true; LockCardScheduler.Instance.Start(); return null;
                case "stop_lock_card": LockCardScheduler.Instance.Stop(); return null;
                case "trigger_haptic":
                    if (CoreHaptics.Service?.IsConnected != true) return "no_device";   // WPF 7b22ece8c (ccp-bugs #1065): never a silent "ok"
                    _ = CoreHaptics.Service!.TriggerAsync("remote_control", 0.7, 2000); return null;
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
                case "enable_strict_lock": s.StrictLockEnabled = true; CoreSettings.Save(); return null;
                case "disable_strict_lock": s.StrictLockEnabled = false; CoreSettings.Save(); return null;
                case "enable_panic": s.PanicKeyEnabled = true; CoreSettings.Save(); SyncPanicUi(); return null;
                case "trigger_panic": StopEffects(force: true); return null;
                default: return NotOnThisBuild;
            }
        }

        /// <summary>WPF StopAllRemoteEffects: everything a controller could start stops; strict off, panic
        /// key on. Only <paramref name="force"/> (trigger_panic) takes the subject's own engine down (#878).</summary>
        public static void StopEffects(bool force)
        {
            if (force) Commands.AiCommandService.CancelAll();   // a remote panic cancels pending AI follow-ups too
            if (force) StopHaptics(); else RemoteHaptics.Stop();
            try { CoreHaptics.Service?.PanicStop(); } catch { }
            CoreAudio.Unduck();
            var s = CoreSettings.Current;
            // WPF :959/:1150 LeavesLocalRun (#1405): a disconnect over the subject's own run leaves its overlays alone;
            // otherwise the remote switches go off with them (WPF :1019-1022, :1179-1182).
            bool clearOverlays = force || !CoreEngine.IsRunning;
            if (clearOverlays && (s.PinkFilterEnabled || s.SpiralEnabled))
            {
                s.PinkFilterEnabled = s.SpiralEnabled = false;
                CoreSettings.Save();
            }
            if (clearOverlays) StopOverlays();
            if (force) CoreEngine.Stop();
            else
            {
                CoreFlash.Stop(); CoreSubliminal.Stop(); CoreBubbles.Stop(); CoreBouncingText.Stop();
                CoreEngine.Video?.Stop(); CoreEngine.BubbleCount?.Stop(); LockCardScheduler.Instance.Stop(); CoreMindWipe.Stop();
            }
            // As WPF, Lockdown or not: this only reduces restraint, and LockdownService.Deactivate restores
            // the pre-lockdown values when the timer ends.
            if (s.StrictLockEnabled || !s.PanicKeyEnabled)
            {
                s.StrictLockEnabled = false;
                s.PanicKeyEnabled = true;
                CoreSettings.Save();
                SyncPanicUi();
            }
        }

        private static void SyncPanicUi() { try { LockdownService.PanicKeyUiSync?.Invoke(); } catch { } }
    }
}
