// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs: OnGlobalKeyPressed's panic
// branch (:955), HandlePanicKeyPress (:1458) and RunPanicStopTail (:1694), engine-not-running path.
// The decision is the same Core PanicPolicy; the listener is Platform/X11PanicKey (XInput2 raw keys,
// non-consuming like the WH_KEYBOARD_LL hook - docs/avalonia-decisions.html, panic key row).
// The stop pass is PanicSurfaces.StopAll (one registry shared with the tray and the safe word); a new
// surface registers there, never here. ponytail: no bark on this head yet. The #919b watchdog is Platform/PanicWatchdog:
// a queued stop the UI thread never runs is torn down off-thread after 2 s.
// The optional Pause key and the EscapeClaim surfaces ride Platform/Win32Input (Windows only).
// Windows: Platform/Win32PanicKey (WH_KEYBOARD_LL on its own thread) feeds the same HandlePanicKeyPress.
// A rebind applies while a portal session is open, as WPF's hook reads the key per press: the 2 s portal
// watch closes the session and binds the new key (RebindPortalIfKeyChanged). A key X cannot hear warns the
// user once (X11PanicKey.Unhearable) instead of going silently dead.

using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;
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
            X11PanicKey.Unhearable = WarnUnhearable;
            return X11PanicKey.Start(() => CoreSettings.Current.PanicKey, OnX11PanicPress);
        }

        /// <summary>One X11 raw press of the panic key (listener thread): the panic, then the leash hold,
        /// unless the portal's report of this press already ran both.</summary>
        internal void OnX11PanicPress()
        {
            Serilog.Log.Information("Panic trigger: XInput2 key press");
            if (!AdmitPanicRoute(PanicRouteGate.Route.X11)) return;
            QueuePanic(() => HandlePanicKeyPress(DateTime.Now));
            Dispatcher.UIThread.Post(OnX11HoldPressed);   // after the panic: hold-to-cut (MainShellWindow.LeashHold.cs)
        }

        /// <summary>One portal Activated (D-Bus thread): a press of its own, or the XInput2 press again.
        /// The echo adds no panic, but the hold still has to become a portal hold.</summary>
        internal void OnPortalPanicActivated()
        {
            try
            {
                if (AdmitPanicRoute(PanicRouteGate.Route.Portal)) QueuePanic(OnPortalPanicPressed);
                else Dispatcher.UIThread.Post(OnPortalEchoPressed);
            }
            catch (Exception ex)   // as the X11 listener does: nothing may throw into the D-Bus reader
            {
                Serilog.Log.Error(ex, "Panic: portal report failed");
            }
        }

        // Either route can win the race, so both are watched (#919b): if the UI thread never runs the
        // press, the watchdog still stops things. A panic that is off or capturing has nothing to tear down.
        private void QueuePanic(Action handler)
        {
            var s = CoreSettings.Current;
            if (!s.PanicKeyEnabled || CapturingPanicKey || LockdownActive || Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey)
                Dispatcher.UIThread.Post(handler);
            else
                PanicWatchdog.QueueWatched(handler);
        }

        // Asked on the listener threads, where a report arrives: the first press's stop pass holds the UI
        // thread for longer than the echo window, so a check after the post would miss the echo.
        private bool AdmitPanicRoute(PanicRouteGate.Route route)
        {
            if (PanicRoutes.Admit(route, PanicRouteClock())) return true;
            Serilog.Log.Information("Panic: {Route} report dropped, the other route already took this press", route);
            return false;
        }

        internal PanicRouteGate PanicRoutes { get; } = new();
        internal Func<long> PanicRouteClock = () => Environment.TickCount64;
        internal int PanicPressCountForTest => _panicPressCount;

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
            // Every press, before the lock-card stop or the capture abort reads as silence.
            try { IntakeHostWindow.StopMicsForPanic(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: stopping the mics failed"); }
            CancelPendingAi();   // every press, even one a lock card or the palette consumes
            // WPF MainWindow.xaml.cs:1655: the welcome show stops before EMI's panic hold, so the hold face is not
            // swallowed by a presentation still on screen. The stop pass's "first-show" row repeats it (idempotent).
            try { WelcomeShow.FirstShowService.Stop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: first show stop failed"); }
            try { EmiDesk.EmiDeskService.Instance.PanicSilence(); } catch (Exception ex) { Serilog.Log.Warning(ex, "EMI panic silence failed"); } // WPF MainWindow.xaml.cs:1620
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
            // WPF MainWindow.xaml.cs:1734: a live Chaos descent outranks both game handoffs.
            // This head stops Chaos through the normal stop pass, including during its countdown.
            if (rung == PanicPolicy.Rung.RunLadder && !Chaos.ChaosRunHost.IsActive)
            {
                // WPF MainWindow.xaml.cs:1741,1763: DtRH before the Back Room, including Breakout.
                // WPF :1806-1829: then the For You feed's two rungs (ghost off, then close). Override still stops all.
                if (Games.GameWindow.HandleDtrhPanic()
                    || Games.GameWindow.HandleBreakoutPanic(now)
                    || Games.GameWindow.ForYouTakesPanicKey())
                {
                    StopCameraForPanic(); // decision C: every panic that stops a surface stops the camera
                    return;
                }
            }
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
            if (wasRunning) App.Achievements?.TrackPanicPressed(now);
            PanicSurfaces.StopAll("panic key", this, aftermathArmed: true);   // armed above (WPF :1662), before any teardown
            NoteLocalPanicPause();   // WPF RunPanicStopTail :1898; remote/voice panic never marks.
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

        // ---- Native Wayland windows: GlobalShortcuts while effects run or a portal hold is live ----
        // Policy C (docs/avalonia-decisions.html, panic key row): the portal grab CONSUMES the key
        // desktop-wide, so it exists while effects run or a portal hold is live. The first effect
        // waits (<= 30 s, a KDE dialog) for the bind; refused/missing/timeout = XI2 only for this
        // run, one notice, and the effect starts anyway.
        private static Task? _portalBind;
        private static bool _portalFailed, _portalAllowed;
        private static DispatcherTimer? _portalWatch;
        private static readonly System.Collections.Generic.Queue<Action> PortalStartQueue = new();

        // The shell's live hold, not PortalPanicShortcut.IsHeld: that stays true after a lost Deactivated.
        internal static bool KeepPortalBound =>
            (PanicRouteWanted && AnyEffectRunning) || PortalLeashSafetyNeeded;

        // WPF MainWindow.xaml.cs:878-881,2329-2330: panic-off and Lockdown retain
        // the leash-only route. Decision C still limits the consuming grab to effects/holds.
        private static bool PanicRouteWanted => CoreSettings.Current.PanicKeyEnabled || LeashHoldLeashed;

        // Decision C: unlike WPF's non-consuming hook (MainWindow.Leash.cs:434-437),
        // a portal grab must not last the whole leash. Only finish an already-live hold.
        private static bool PortalLeashSafetyNeeded => PortalHoldLive;

        private static bool AnyEffectRunning =>
            PortalStartQueue.Count > 0 || CoreEngine.IsRunning || CoreFlash.IsRunning || CoreSubliminal.IsRunning || BouncingTextOverlay.IsRunning
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

        // WPF MainWindow.xaml.cs:2248 / :2317 (SyncNoPanicState), wired to Lockdown in
        // App.xaml.cs:3447. Observe the setting itself so every writer restores the global route.
        private void HookPanicEnabled()
        {
            AppSettings? hooked = null;
            var service = CoreSettings.Service;
            bool closed = false;
            bool? lastEnabled = null;
            void Rebind()
            {
                if (closed) return;
                if (hooked != null) hooked.PropertyChanged -= Changed;
                hooked = CoreSettings.Current;
                hooked.PropertyChanged += Changed;
                Sync();
            }
            void Changed(object? sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppSettings.PanicKeyEnabled))
                    Sync();
            }
            void Sync()
            {
                bool enabled = CoreSettings.Current.PanicKeyEnabled;
                bool reenabled = lastEnabled == false && enabled;
                lastEnabled = enabled;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!closed) SyncPanicEnabled(reenabled);
                });
            }
            void Replaced() => Dispatcher.UIThread.Post(Rebind);
            Rebind();
            if (service != null) service.CurrentReplaced += Replaced;
            Closed += (_, _) =>
            {
                closed = true;
                if (hooked != null) hooked.PropertyChanged -= Changed;
                if (service != null) service.CurrentReplaced -= Replaced;
            };
        }

        private static readonly System.Threading.SemaphoreSlim PanicBindingSync = new(1, 1);

        private static async void SyncPanicEnabled(bool reenabled)
        {
            await PanicBindingSync.WaitAsync();
            try
            {
                // Let an in-flight portal dialog finish before closing/rebinding its session.
                // Read the latest setting after the await: a quick off/on must not lose its bind.
                if (_portalBind != null) await _portalBind;
                // An explicit settings transition gets a fresh attempt after an earlier refusal.
                if (!PortalPanicShortcut.IsBound) _portalBind = null;
                if (reenabled) _portalFailed = false;
                if (!PanicRouteWanted && !PortalLeashSafetyNeeded)
                {
                    _portalWatch?.Stop();
                    _portalWatch = null;
                    await PortalPanicShortcut.UnbindAsync();
                    _portalBind = null;
                }
                if (KeepPortalBound)
                    StartEffect(() => { });
            }
            finally { PanicBindingSync.Release(); }
        }

        internal static void StartEffect(Action start)
        {
            if (PortalStartQueue.Count == 0 && ((!_portalAllowed && PortalPanicShortcut.BindForTests == null)
                || (PortalPanicShortcut.IsBound && _portalBind is not { IsCompleted: false }) || _portalFailed
                || (!PanicRouteWanted && !PortalLeashSafetyNeeded)
                || (PortalPanicShortcut.BindForTests == null && (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))))))
            {
                start();
                return;
            }
            PortalStartQueue.Enqueue(start);
            if (PortalStartQueue.Count != 1) return;
            _portalBind ??= BindPortalAsync();
            // Post as soon as binding completes, including an already-completed bind. Leaving the
            // continuation on the thread pool can keep the portal alive after the last effect stops.
            _portalBind.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                while (PortalStartQueue.Count > 0)
                {
                    try { PortalStartQueue.Dequeue()(); }
                    catch (Exception ex) { Serilog.Log.Error(ex, "Deferred effect start failed"); }
                }
            }), System.Threading.CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /// <summary>The bound panic key cannot fire on this desktop (listener thread): say so, never only in the
        /// log. The tray's Stop everything still works.</summary>
        internal static void WarnUnhearable(string? key) => Dispatcher.UIThread.Post(() =>
            App.Notifications.Show(Loc.GetF("panic_key_unhearable", key ?? ""), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(12)));

        // The key the open (or opening) portal session was asked for.
        private static string? _portalKey;
        private static bool _portalRebinding;
        internal static string? PortalKeyForTest => _portalKey;

        private static async Task BindPortalAsync()
        {
            var key = _portalKey = CoreSettings.Current.PanicKey;
            var trigger = await PortalPanicShortcut.BindAsync(key, Loc.Get("panic_portal_description"),
                () =>
                {
                    Serilog.Log.Information("Panic trigger: GlobalShortcuts portal Activated");
                    Current?.OnPortalPanicActivated();
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
                // 2 s poll for the last effect/hold ending rather than a hook in every stop path.
                _portalWatch ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
                    (_, _) => ReleasePortalWhenUnused());
                _portalWatch.Start();
            });
        }

        // Shared with the headless tests so they exercise the real idle-release poll.
        internal static void ReleasePortalWhenUnused(MainShellWindow? shell = null)
        {
            if (KeepPortalBound) { RebindPortalIfKeyChanged(shell); return; }
            _portalWatch?.Stop();
            _portalWatch = null;
            _portalBind = null;
            _ = PortalPanicShortcut.UnbindAsync();
            (shell ?? Current)?.PanicRoutes.Forget();   // the next press reaches XInput2 alone, inside a fast double press
        }

        /// <summary>WPF reads the panic key on every press (MainWindow.xaml.cs:943-960), so a rebind is live at once.
        /// The portal grab holds the key it was opened for: once the capture is over and no hold rides the old key,
        /// close that session and bind the new one, serialised with <see cref="SyncPanicEnabled"/>. XInput2 keeps
        /// hearing X11 windows throughout (it re-resolves per event); a refused re-bind is the same notice as a
        /// first refusal, and the run stays on XInput2 alone.</summary>
        internal static void RebindPortalIfKeyChanged(MainShellWindow? shell = null)
        {
            if (_portalRebinding || !NeedsPortalRebind()) return;
            _portalRebinding = true;
            _ = RebindPortalAsync(shell);
        }

        private static bool NeedsPortalRebind() =>
            PortalPanicShortcut.IsBound && !_portalFailed && !PortalHoldLive && !CapturingPanicKey
            && !string.Equals(_portalKey, CoreSettings.Current.PanicKey, StringComparison.Ordinal);

        private static async Task RebindPortalAsync(MainShellWindow? shell)
        {
            await PanicBindingSync.WaitAsync();
            try
            {
                if (_portalBind != null) await _portalBind;
                if (!NeedsPortalRebind() || !KeepPortalBound) return;
                Serilog.Log.Information("Panic key: rebound to '{Key}' while the portal is bound; re-binding", CoreSettings.Current.PanicKey);
                _portalWatch?.Stop();
                _portalWatch = null;
                await PortalPanicShortcut.UnbindAsync();
                _portalBind = null;
                (shell ?? Current)?.PanicRoutes.Forget();   // the old session can no longer echo an XInput2 press
                StartEffect(() => { });
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Panic key: portal re-bind failed"); }
            finally
            {
                _portalRebinding = false;
                PanicBindingSync.Release();
            }
        }

        /// <summary>WPF LockCardService.Stop(dismissOpenCards: true).</summary>
        internal static void StopLockCards()
        {
            LockCardWindow.ForceCloseAll();
            LockCardScheduler.Instance.Stop();
        }
    }
}
