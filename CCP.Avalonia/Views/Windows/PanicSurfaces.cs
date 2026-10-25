// PORTED from ConditioningControlPanel/Services/Safety/GameSurfaces.cs and MainWindow.xaml.cs
// PanicStopEverySurface (:1934): one list of everything a panic stops, so a new surface is one line here
// instead of a call remembered in each route (P06). The panic key, the tray's Stop everything and the
// spoken safe word all call StopAll; Lockdown refusal, the lock-card/palette/grace-pause rungs, the
// window restore and the double-press exit ladder stay in the routes. Guarded by PanicSurfacesTests.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    internal static class PanicSurfaces
    {
        /// <summary>A thing a panic must stop. <paramref name="OwnsTheScreen"/> marks a game-like host:
        /// a press that ends it does not advance the double-press exit ladder (P30, WPF
        /// AnyGameSurfaceOwnsTheScreen). <paramref name="Stop"/> gets the shell the route ran on
        /// (null off the desktop path) and must be idempotent and safe when idle. <paramref name="After"/>
        /// names the surfaces that must stop before this one (a contract, cite WPF or the decision next to it);
        /// PanicSurfacesTests enforces it.</summary>
        internal sealed record Surface(string Id, Action<MainShellWindow?> Stop, Func<bool>? OwnsTheScreen = null, string[]? After = null);

        /// <summary>In stop order; order matters where an entry names <c>After</c>. Add a surface with one
        /// line (and its id to PanicSurfacesTests.SafetyCritical). WPF refs are
        /// ConditioningControlPanel/MainWindow/MainWindow.xaml.cs unless named otherwise. Tests swap it.</summary>
        internal static IReadOnlyList<Surface> All { get; set; } = new Surface[]
        {
            // Mic first (decisions "Panic ↔ mic"): the capture and command chain in flight end before
            // anything can react to them; the wake loop and push-to-talk stay armed.
            // The intake's say-it loops end before the capture abort below reads as silence; WPF GameSurfaces
            // 'intake' -> CloseActive. A game surface: closing it never arms the exit ladder.
            new("intake", _ => IntakeHostWindow.CloseAllForPanic(), IntakeHostWindow.IsAnyOpen),
            // WPF LeashOnPanicPress, the first thing a panic press does: the leash task parks (kept pending), its
            // video window closes, the gate stands back ten minutes.
            new("leash-confirm", _ => Controls.Leash.LeashCutConfirmWindow.CloseForPanic()),
            new("leash-task", sh => Platform.LeashTaskHost.OnPanicPress(sh)),
            new("tour", _ => Tours.TutorialHead.OnPanic()),                // a guided tour ends by the abandon route (never latched as walked)
            // WPF GameSurfaces.cs:53-55 backroom/breakout: every web game window (Views/Games/GameWindow).
            new("games", _ => Games.GameWindow.CloseAllForPanic(), Games.GameWindow.IsAnyOpen),
            new("first-show", _ => WelcomeShow.FirstShowService.Stop()),   // WPF MainWindow.xaml.cs:1553, before CancelPendingAi
            new("voice-capture", sh => sh?.CancelVoicePrompt(), After: ["intake"]),   // intake loops end before the mic abort
            // decisions "Panic <-> mic": capture and the command chain end first; WPF :1553 FirstShowService.Stop() before CancelPendingAi.
            new("ai-followups", _ => MainShellWindow.CancelPendingAi(), After: ["voice-capture", "first-show"]),
            // WPF MainWindow.xaml.cs:1732: standalone Lab minigames first; the engine stop never reaches them.
            new("blink-trainer", _ => Overlays.BlinkTrainerSession.Stop()),
            new("gaze-minigame", _ => Lab.GazeMinigame.GazeMinigameWindow.CloseAllForPanic(), Lab.GazeMinigame.GazeMinigameWindow.IsAnyRunning),   // ends before the camera stop below
            new("mantra", _ => MantraWindow.StopForPanic()),                // WPF KillAllAudio -> Mantra?.Dispose()
            new("chaos", _ => Chaos.ChaosRunHost.ForceShutdown(), () => Chaos.ChaosRunHost.IsDescending),
            new("chaos-flash", _ => Overlays.ChaosFlashOverlay.CloseActive()),   // WPF ForceShutdown :3175; a run-less glitch wash too
            // WPF ChaosModeService.ForceShutdown (:3176) closes the rain; it also runs outside a run (trigger bubble, Emi, BackRoom).
            new("gif-cascade", _ => Chaos.ChaosGifCascadeOverlay.CloseActive()),
            // WPF ForceShutdown :3177 ChaosAnnouncerOverlay.CloseActive: queue dropped, window closed, run or not.
            new("chaos-announcer", _ => Chaos.ChaosAnnouncerOverlay.CloseActive()),
            new("chaos-dvd", _ => Chaos.ChaosDvdOverlay.CloseActive()),   // WPF ForceShutdown -> ChaosDvdOverlay.CloseActive (:3179)
            new("chaos-cursor-glow", _ => Chaos.ChaosCursorGlowOverlay.CloseActive()),   // WPF ForceShutdown :3183 closes the halo
            new("chaos-vibe-trail", _ => Chaos.ChaosVibeTrailOverlay.CloseActive()),   // WPF ForceShutdown :3184; also when no run owns it
            new("chaos-estim", _ => Chaos.ChaosEStimOverlay.CloseActive()),   // lightning + cursor halo (WPF ChaosModeService.cs:3185)
            new("chaos-field-fx", _ => Overlays.ChaosFieldFxOverlay.CloseActive()),   // WPF ChaosModeService teardown CloseActive
            new("chaos-skia-fx", _ => Chaos.ChaosSkiaFxOverlay.CloseActive()),   // WPF ForceShutdown -> ChaosSkiaFxOverlay.CloseActive; also outside a run
            new("chaos-intro", _ => Chaos.ChaosIntroWindow.CloseForPanic()),   // the modal first-run card (WPF left it up)
            // WPF PanicStopEverySurface (:2003): the toys go to zero, bypassing throttles and gates.
            new("haptics", _ => CoreHaptics.Service?.PanicStop()),
            new("remote-haptics", _ => RemoteCommands.StopHaptics()),   // decisions 2026-10-08
            new("takeover", sh => sh?.StopAutonomyForPanic()),           // WPF KillAllAudio -> Autonomy.Stop
            // WPF RunPanicStopTail StopEngine/StopAdHocEffects: pauses a running session; OnEngineStopped
            // then ends Takeover pulses, desktop overlays (subliminal/whisper, mind wipe, spiral, video,
            // bubbles, pink filter), pop quizzes and open lock cards.
            // programs 3a (P06, docs/avalonia-decisions.md): today's program session ENDS, not pauses, so
            // a panic never leaves a program run holding the screen; foreign sessions stay paused below.
            new("program-session", sh => { if (sh != null) sh.EndProgramSessionQuietly("panic"); else App.Programs?.StopProgramSessionIfRunning("panic"); }),
            // WPF :1554 CancelPendingAi (right after FirstShowService.Stop :1553) runs long before StopEngine (:1838); :1721-1732 game
            // surfaces and Lab minigames (GameSurfaces.cs:50 'chaos') close before RunPanicStopTail (:1733); :1819 KillAllAudio
            // (App.xaml.cs:1780 Mantra.Dispose), :1797 remote haptics, :2003 haptics PanicStop and :1822 Autonomy.CancelActivePulses
            // all precede StopEngine. WPF MainWindow.Leash.cs LeashOnPanicPress is the first thing a panic press does,
            // so the leash task parks before StopEngine and before its lock card closes.
            new("engine", _ => MainShellWindow.StopEngine(),
                After: ["ai-followups", "blink-trainer", "gaze-minigame", "chaos", "mantra", "haptics", "remote-haptics", "takeover", "leash-task"]),
            // WPF StopEngine -> MainWindow.StartStop.cs:505 App.SkillTree?.Stop(): the 3x ends with/after the engine.
            new("pink-rush", _ => PinkRushHost.Stop(), After: ["engine"]),                 // WPF StopEngine -> SkillTree.Stop: the 3x ends
            // WPF :1978 "corner GIFs": the standalone Spiral-card slots close (settings untouched) and queued ones cancel;
            // :1974-1978 the engine-owned session GIF goes first, these standalone slots after it.
            new("corner-gif", _ => Overlays.CornerGifOverlay.StopAll(), After: ["engine"]),
            // WPF :1981-1982 "tube speech"/"avatar voice": cut her line, drop the queue/animations and the bubble.
            // WPF :1978-1981: after the corner GIFs.
            new("tube-speech", sh => (sh?.Tube ?? AvatarTube.AvatarTubeWindow.Live)?.PanicSilence(), After: ["corner-gif"]),
            // WPF LockCardService.Stop(dismissOpenCards: true); :2041 StopAdHocEffects closes lock cards after the engine/media stop.
            new("lock-cards", _ => MainShellWindow.StopLockCards(), After: ["engine", "leash-task"]),
            new("speak-cue", _ => Overlays.SpeakCueOverlay.CloseActive()),   // WPF: Deeper stop -> SpeakPromptSession.StopCue -> HideCue
            new("companion-transcript", _ => Controls.Companion.CompanionTranscriptWindow.CloseForPanic()),   // P06; WPF left the History modal up
            new("possession", _ => Possession.PossessionRuntime.UndoAllLive()),   // every touched control gets its own transform back now (WPF PossessionDirector.UndoAll)
            new("attention-test", _ => Overlays.AttentionTestTarget.CloseAll()),   // the style editor's Test target (P06; WPF left it up)
            new("deeper-editor-audio", _ => Deeper.DeeperEditorWindow.PauseAllForPanic()),   // P06; WPF left it playing
            new("knock-cards", _ => Overlays.KnockCard.FoldAll()),   // P06; WPF left them up. Folded as Later: they wait in the Inbox
            new("camera", _ => MainShellWindow.StopCameraForPanic(), After: ["gaze-minigame"]),   // decision C: last, fire-and-forget; gaze reads it
        };

        /// <summary>WPF AnyGameSurfaceOwnsTheScreen. Sample BEFORE <see cref="StopAll"/>.</summary>
        internal static bool AnyOwnsTheScreen()
        {
            foreach (var s in All)
            {
                try { if (s.OwnsTheScreen?.Invoke() == true) return true; }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: {Id} screen probe failed", s.Id); }
            }
            return false;
        }

        /// <summary>Stops every surface in order. One failing stop never skips the rest. Never throws.</summary>
        internal static void StopAll(string reason, MainShellWindow? shell = null)
        {
            Serilog.Log.Information("Panic: stopping every surface ({Reason})", reason);
            shell ??= MainShellWindow.Current;
            foreach (var s in All)
            {
                try { s.Stop(shell); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: {Id} stop failed", s.Id); }
            }
        }
    }
}
