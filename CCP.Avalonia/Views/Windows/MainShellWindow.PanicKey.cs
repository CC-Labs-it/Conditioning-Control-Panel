// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs: OnGlobalKeyPressed's panic
// branch (:955), HandlePanicKeyPress (:1458) and RunPanicStopTail (:1694), engine-not-running path.
// The decision is the same Core PanicPolicy; the listener is Platform/X11PanicKey (XInput2 raw keys,
// non-consuming like the WH_KEYBOARD_LL hook - docs/avalonia-decisions.html, panic key row).
// The stop pass is PanicSurfaces.StopAll (one registry shared with the tray and the safe word); a new
// surface registers there, never here. ponytail: no bark on this head yet. The #919b watchdog is Platform/PanicWatchdog:
// a queued stop the UI thread never runs is torn down off-thread after 2 s.
// The optional Pause key and the EscapeClaim surfaces ride Platform/Win32Input (Windows only).
// Windows: Platform/Win32PanicKey (WH_KEYBOARD_LL on its own thread) feeds the same HandlePanicKeyPress.
// ponytail: a rebind while a portal session is open keeps the OLD trigger until the effects stop and
// the next effect re-binds; re-bind on PanicKey change if that matters.

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF _isCapturingPanicKey: Settings/Devices is waiting for the key to bind, so a
        /// press is a rebind, never a panic.</summary>
        internal static bool CapturingPanicKey { get; set; }

        private int _panicPressCount;
        private DateTime _lastPanicTime = DateTime.MinValue;

        /// <summary>App calls this once on the desktop path (never in tests or renders, so only the
        /// real app ever talks to the portal). False when no global listener is up.</summary>
        internal bool StartPanicKey()
        {
            _portalAllowed = true;
            SeedPanicAftermath();
            // WPF EscapeClaim surfaces (TAB-8 / DESK-3): the focus snapshot the hook thread reads.
            EscapeClaim.MarkType(typeof(Views.Controls.FriendsDrawer));
            EscapeClaim.StartTracking();
            if (OperatingSystem.IsWindows()) return Win32Input.Start(() => HandlePanicKeyPress(DateTime.Now));   // WH_KEYBOARD_LL twin
            return X11PanicKey.Start(() => CoreSettings.Current.PanicKey, OnX11PanicPress);
        }

        /// <summary>One X11 raw press of the panic key (listener thread): the panic, then the leash hold.</summary>
        internal void OnX11PanicPress()
        {
            Serilog.Log.Information("Panic trigger: XInput2 key press");
            var s = CoreSettings.Current;
            if (!s.PanicKeyEnabled || CapturingPanicKey || LockdownActive || Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey)
                Dispatcher.UIThread.Post(() => HandlePanicKeyPress(DateTime.Now));
            else
                PanicWatchdog.QueueWatched(() => HandlePanicKeyPress(DateTime.Now));   // #919b
            Dispatcher.UIThread.Post(LeashHoldPressed);   // after the panic: hold-to-cut (MainShellWindow.LeashHold.cs)
        }

        /// <summary>One press of the configured key (UI thread). A foreground board or DTRH may keep the first
        /// Escape as pause; unclaimed presses stop everything. A second
        /// counted press within 2 s exits the app, as WPF does when no engine is running.</summary>
        internal void HandlePanicKeyPress(DateTime now)
        {
            var s = CoreSettings.Current;
            // WPF MainWindow.xaml.cs:914: a Pause-key capture takes the press before the panic branch, so the
            // Escape that unbinds Pause (the default panic key) is never also a panic.
            if (CapturingPanicKey || Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey) return;
            // WPF MainWindow.xaml.cs:888: Lockdown ignores every GLOBAL key, whatever LockdownDisablePanicKey
            // says. Only this listener layer: window and TextBox input (the secret phrase) are untouched.
            // Panic off is the same refusal. Both share Core VoicePanicReach.PanicKeyRuns with the voice
            // panic and the blink stop (hard rule 6: no stop is more permissive than this key).
            if (!VoicePanicReach.PanicKeyRuns(LockdownActive, s.PanicKeyEnabled))
            {
                if (LockdownActive) Serilog.Log.Information("Panic key ignored under Lockdown");
                // WPF LeashPanicKeyWhilePanicOff: panic always works on a leash. The leash part alone runs
                // (the task parks, a lines card it opened closes, its window closes, the gate stands back);
                // the session, effects and exit ladder are untouched. The hold-to-cut rides LeashHoldPressed.
                if (LeashHoldLeashed) PanicKeyLeashOnly(fromKey: true);
                return;
            }
            // WPF MainWindow.xaml.cs:1557: a game's pause, BEFORE everything below: a pause is not a panic.
            if (TryGamePauseOnEscape(now)) return;
            IntakeHostWindow.StopMicsForPanic();   // every press, before the lock-card stop or the capture abort reads as silence
            CancelPendingAi();   // every press, even one a lock card or the palette consumes
            // WPF MainWindow.xaml.cs:1620: Circe's Tab safety hold, armed up here because the lock-card, palette and
            // grace-pause rungs below return before the stop pass ("chaster-hold"). A game's pause above is no panic.
            PanicChasterHold.Open("panic key");
            // WPF MainWindow.xaml.cs:1567 LeashOnPanicPress, before the lock-card rung: the leash task parks first,
            // or a Lines task reopens the card the press just dismissed. fromKey: only this physical press may be the
            // Escape that closes the Ctrl+K palette (the stop pass's own "leash-task" never skips).
            try { Platform.LeashTaskHost.OnPanicPress(this, fromKey: true); } catch (Exception ex) { Serilog.Log.Warning("Leash panic failed: {E}", ex.Message); }

            // Same evaluation order as WPF: asking the palette closes it, so never ask with a card up.
            bool lockCardOpen = LockCardWindow.IsAnyOpen();
            bool paletteClaimed = !lockCardOpen
                && string.Equals(s.PanicKey, "Escape", StringComparison.OrdinalIgnoreCase)
                && SettingsPaletteWindow.TryConsumeEscape();
            var rung = PanicPolicy.Decide(lockCardOpen, paletteClaimed, PanicPolicy.OverrideEnabled(s));
            Serilog.Log.Information("Panic key pressed ({Rung})", rung);
            // WPF MainWindow.xaml.cs:1662: the post-panic quiet (PanicAftermath), armed before any rung can return
            // (lock card, grace pause). Not for an Escape that only closed the Ctrl+K palette: it stops nothing.
            if (rung != PanicPolicy.Rung.DismissSettingsPalette) PanicAftermath.ArmQuiet();
            if (rung == PanicPolicy.Rung.DismissLockCard) { StopLockCards(); StopCameraForPanic(); }
            if (!PanicPolicy.StopsSurfaces(rung)) return;
            // WPF MainWindow.xaml.cs:1742-1747: the legacy room ladder consumes this key
            // before the stop pass and app exit counter. Escape-pause already had first claim above.
            if (rung == PanicPolicy.Rung.RunLadder && (Games.GameWindow.TryDtrhPanic() || Games.GameWindow.TryBackRoomPanic())) return;
            // WPF MainWindow.xaml.cs:1709: close the game surface that owns the screen, then the normal
            // stop tail with the exit ladder NOT armed (and no grace pause), in BOTH modes: override
            // (MainWindow.xaml.cs:1627) and ladder (:1720 RunPanicStopTail(advanceExitLadder: false)).
            // Sampled BEFORE the stop pass closes it (intake host, Chaos descent).
            bool gameOwnedTheScreen = PanicSurfaces.AnyOwnsTheScreen();
            // WPF #735: with PanicOverridesAll off (RunLadder), the first press over a playing mandatory
            // video grace-pauses it instead (TryGracePause refuses when panic overrides all), and the
            // window's own handling of the same keystroke is deduped there. Not a ladder rung.
            if (!gameOwnedTheScreen && rung == PanicPolicy.Rung.RunLadder && Views.Overlays.MandatoryVideoOverlay.Instance.TryGracePause(fromPanicKey: true))
            {
                Serilog.Log.Information("Panic press consumed as video grace pause");
                StopCameraForPanic();
                return;
            }
            // WPF MainWindow.xaml.cs:1755: ladder mode only (the override rung and game hand-offs return first),
            // after the grace pause and before the stop pass: her calm safety line, then 6 s of no other bark.
            if (rung == PanicPolicy.Rung.RunLadder && !gameOwnedTheScreen) Platform.BarkHead.Engine?.Raise("Panic");
            bool wasRunning = CoreEngine.IsRunning;
            PanicSurfaces.StopAll("panic key", this);
            if (wasRunning) ShowFromTray();   // WPF: Show + Activate the main window after a running stop

            if ((now - _lastPanicTime).TotalMilliseconds > 2000) _panicPressCount = 0;
            // A game surface (intake host or a Chaos descent) owned the screen: the press ends it, no exit rung.
            if (PanicPolicy.AdvancesExitLadder(rung) && !gameOwnedTheScreen) { _panicPressCount++; _lastPanicTime = now; }
            if (_panicPressCount >= 2)
            {
                Serilog.Log.Information("Double panic! Exiting application...");
                ExitWithoutBill();   // WPF MainWindow.xaml.cs:1841 shuts down directly, no bill
            }
        }

        /// <summary>WPF App.AiEffects.cs:19-20: the AI command gate reads the post-panic quiet and the panic count
        /// (Core PanicAftermath, armed by every panic route on this head). Desktop path only, like the listener.</summary>
        internal static void SeedPanicAftermath()
        {
            ConditioningControlPanel.Services.Commands.AiCommandService.PanicQuietProvider = () => PanicAftermath.TriggersSilenced();
            ConditioningControlPanel.Services.Commands.AiCommandService.PanicCountProvider = () => PanicAftermath.PanicCount;
        }

        /// <summary>WPF LeashPanicKeyWhilePanicOff (MainWindow.Leash.cs): the panic key while leashed when the
        /// panic itself cannot run (switched off, or Lockdown holding the keys). Also the voice panic's
        /// <see cref="VoicePanicReach.Reach.LeashOnly"/>, which never skips for the palette (<paramref name="fromKey"/>
        /// false: "red" is no Escape). Never throws.</summary>
        internal void PanicKeyLeashOnly(bool fromKey = false)
        {
            Serilog.Log.Information("Panic while leashed with the panic unable to run - leash safety only");
            try { Platform.LeashTaskHost.OnPanicPress(this, panicRuns: false, fromKey: fromKey); }
            catch (Exception ex) { Serilog.Log.Warning("Leash panic-off stop failed: {E}", ex.Message); }
        }

        private DateTime? _lastGameEscapeClaim;

        /// <summary>WPF TryRacePauseOnEscape (MainWindow.xaml.cs:1505): Escape in front of Racing Thoughts, Piece by
        /// Piece, Breakout (the demo on this head), Down the Rabbit Hole or the Graded Intake is its pause, not a
        /// panic (owner, 2026-10-12: Escape only pauses the game). Every condition is Core
        /// PanicPolicy.GameClaimsEscapeAsPause, including a second Escape within 2 s being a full panic (which
        /// closes the game through PanicSurfaces "games" / "intake"), and main 251b60807's frontGameReady: a game
        /// whose page is still booting (or on its way out) cannot pause, so its Escape is a full panic. The kept
        /// press is handed to the page as well (each page's host-escape.js), since its own keydown never comes
        /// while the web view is out of focus. Only a real key press reaches here (the HUD and leash gate buttons
        /// call PanicSurfaces directly), so fromPanicKey is always true on this head.
        /// Not hosted on this head yet, so never in front: the full Breakout, the Arcademy, the Back Room room, Goon.</summary>
        internal bool TryGamePauseOnEscape(DateTime now)
        {
            try
            {
                var game = Games.GameWindow.EscapePauseInFront();
                var page = game?.KeptEscapePage ?? PanicPolicy.KeptEscapePage.None;
                bool ready = game?.KeptEscapeReady == true;
                var intake = game == null ? IntakeHostWindow.InFront() : null;
                bool intakeReady = intake?.KeptEscapeReady == true;
                bool In(PanicPolicy.KeptEscapePage p) => page == p;
                PanicPolicy.KeptEscapePage Route(bool claimed) => PanicPolicy.KeptEscapeGoesTo(claimed,
                    In(PanicPolicy.KeptEscapePage.Race), ready, In(PanicPolicy.KeptEscapePage.Board), ready,
                    In(PanicPolicy.KeptEscapePage.Breakout), ready, In(PanicPolicy.KeptEscapePage.Dtrh), ready,
                    roomInFront: In(PanicPolicy.KeptEscapePage.Room), roomReady: ready,
                    intakeInFront: intake != null, intakeReady: intakeReady,
                    glassroadInFront: In(PanicPolicy.KeptEscapePage.Glassroad), glassroadReady: ready);
                bool claim = PanicPolicy.GameClaimsEscapeAsPause(CoreSettings.Current.PanicKey,
                    gameInFront: game != null || intake != null, engineRunning: CoreEngine.IsRunning,
                    lockCardOpen: LockCardWindow.IsAnyOpen(), lastClaimUtc: _lastGameEscapeClaim, nowUtc: now,
                    frontGameReady: Route(true) != PanicPolicy.KeptEscapePage.None, fromPanicKey: true);
                if (!claim) { _lastGameEscapeClaim = null; return false; }
                _lastGameEscapeClaim = now;
                Serilog.Log.Information("Panic: Escape kept by {Game} as its pause (again within 2 s = full panic)",
                    game?.Spec.Id ?? "intake");
                switch (Route(claim))
                {
                    case PanicPolicy.KeptEscapePage.Intake: intake!.PostKeptEscape(); break;
                    case PanicPolicy.KeptEscapePage.None: break;
                    default: game!.PostKeptEscape(); break;
                }
                return true;
            }
            catch (Exception ex)
            {
                // A probe that throws must never eat a panic press.
                Serilog.Log.Warning("Panic: game pause probe failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>Decision C (docs/avalonia-decisions.html, panic and the camera; deliberate WPF deviation):
        /// every panic press that is not a palette-claimed Escape closes the camera, after the audio and
        /// overlay teardown. Fire-and-forget: Stop can block up to 5 s on a wedged driver. Consent,
        /// calibration, device choice and settings are kept. The Blink Trainer session is stopped with
        /// it (it has nothing to swap on without blinks), which also cancels a queued session start;
        /// the tracker's own generation stops a start already in flight from publishing its camera.</summary>
        internal static void StopCameraForPanic()
        {
            Views.Overlays.BlinkTrainerSession.Stop();
            var tracker = Platform.WebcamTracker.Instance;
            bool on = tracker.IsRunning || tracker.IsStarting;
            _ = tracker.StopAsync();
            if (on) App.Notifications.Show(Loc.Get("panic_camera_stopped"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(5));
        }

        // ---- Native Wayland windows: the GlobalShortcuts portal, bound only while effects run ----
        // Policy C (docs/avalonia-decisions.html, panic key row): the portal grab CONSUMES the key
        // desktop-wide, so it exists only while a desktop effect is on screen. The first effect
        // waits (<= 30 s, a KDE dialog) for the bind; refused/missing/timeout = XI2 only for this
        // run, one notice, and the effect starts anyway.
        private static Task? _portalBind;
        private static bool _portalFailed, _portalAllowed;
        private static DispatcherTimer? _portalWatch;

        // The shell's live hold, not PortalPanicShortcut.IsHeld: that stays true after a lost Deactivated.
        internal static bool KeepPortalBound => AnyEffectRunning || PortalHoldLive;

        private static bool AnyEffectRunning =>
            CoreEngine.IsRunning || CoreFlash.IsRunning || CoreSubliminal.IsRunning || BouncingTextOverlay.IsRunning
            || Views.Overlays.BlinkTrainerSession.IsRunning;

        /// <summary>Every desktop-effect start goes through here. <paramref name="start"/> must re-check
        /// that the effect is still wanted: it can run up to 30 s later, after a panic or an untick.</summary>
        /// <summary>Every panic route calls this: pending AI follow-ups are cancelled and
        /// AiCommandService.CancelGeneration moves on, so an AI start deferred in <see cref="StartEffect"/>
        /// is dropped. Never throws.</summary>
        internal static void CancelPendingAi()
        {
            try { ConditioningControlPanel.Services.Commands.AiCommandService.CancelAll(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "AI command cancel on panic failed"); }
        }

        internal static void StartEffect(Action start)
        {
            if (!_portalAllowed || PortalPanicShortcut.IsBound || _portalFailed || !CoreSettings.Current.PanicKeyEnabled
                || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                start();
                return;
            }
            _portalBind ??= BindPortalAsync();
            _portalBind.ContinueWith(_ => Dispatcher.UIThread.Post(start), TaskScheduler.Default);
        }

        private static async Task BindPortalAsync()
        {
            var trigger = await PortalPanicShortcut.BindAsync(CoreSettings.Current.PanicKey, Loc.Get("panic_portal_description"),
                () =>
                {
                    Serilog.Log.Information("Panic trigger: GlobalShortcuts portal Activated");
                    Dispatcher.UIThread.Post(() => Current?.OnPortalPanicPressed());
                },
                () => Dispatcher.UIThread.Post(() => Current?.OnPortalPanicReleased()));
            Dispatcher.UIThread.Post(() =>
            {
                if (trigger == null)
                {
                    _portalFailed = true;
                    App.Notifications.Show(Loc.Get("panic_portal_unavailable"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    return;
                }
                // trigger_description: what the compositor really bound, which the user may have changed.
                // Empty = the compositor assigned no key, so never claim the configured one.
                if (trigger.Length > 0)
                    App.Notifications.Show(Loc.GetF("panic_portal_bound", trigger), Helpers.NotificationType.Info, TimeSpan.FromSeconds(6));
                else
                    App.Notifications.Show(Loc.Get("panic_portal_unavailable"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
                // ponytail: 2 s poll for "last effect stopped" rather than a hook in every stop path.
                _portalWatch ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
                {
                    if (KeepPortalBound) return;
                    _portalWatch!.Stop();
                    _portalWatch = null;
                    _portalBind = null;
                    _ = PortalPanicShortcut.UnbindAsync();
                });
                _portalWatch.Start();
            });
        }

        /// <summary>WPF LockCardService.Stop(dismissOpenCards: true).</summary>
        internal static void StopLockCards()
        {
            LockCardWindow.ForceCloseAll();
            LockCardScheduler.Instance.Stop();
        }
    }
}
