using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// This head's half of WPF's <c>App.StartupLadder</c> (Services/Startup/StartupPresenter.cs): the
    /// passive route and the Inbox. A passive surface opens now unless something already owns the
    /// user (session, tour, the ten-minute first-launch window), in which case it becomes an Inbox
    /// row - the same Core rule WPF reads (<see cref="StartupQueueCore.Route"/>).
    /// Passive surfaces open one at a time (WPF 8cbcbea01): while a feature card or an
    /// announcement is up, or one opened under <see cref="StartupQueueCore.PassiveSettle"/> ago,
    /// the next waits and is re-routed once it has gone.
    /// WPF Services/Startup/StartupPresenter.cs:247,350,554: defer until the modal queue drains.
    /// Modal offers share the Core priority queue (<see cref="EnqueueModal"/>, WPF StartupPresenter.cs:89)
    /// and park while the launcher hold or a game is up. Upgrade What's New and the online licence
    /// card still hold passive offers through ModalPending until they move onto the queue.
    /// </summary>
    internal static class StartupLadder
    {
        // WPF StartupPresenter.cs:133,140; LauncherHost.cs:266 releases on panel open.
        internal static bool LauncherHolding { get; private set; }
        internal static void Hold() => LauncherHolding = true;
        internal static void Release() => LauncherHolding = false;
        internal static bool ModalPending;
        internal static int PresentationGeneration { get; private set; }

        public static StartupInbox Inbox { get; } = new();

        private static DateTime? _firstLaunchUntilUtc;
        private static DateTime? _lastPassiveUtc;
        private static readonly List<InboxItem> _held = new();
        private static ITimer? _passiveTimer;
        private static readonly StartupQueueCore _modalQueue = new();
        private static readonly Dictionary<string, (Func<Task> Show, Action? Abandoned)> _modalShows = new(StringComparer.OrdinalIgnoreCase);
        private static bool _modalPumpRunning, _modalUp;
        private static CancellationTokenSource _modalCancellation = new();
        internal static Func<bool> ModalWindowReady = () => Views.Windows.MainShellWindow.Current?.IsVisible == true;
        internal static readonly TimeSpan ModalSettle = TimeSpan.FromMilliseconds(750);
        internal static readonly TimeSpan ModalPoll = TimeSpan.FromMilliseconds(500);
        internal static readonly TimeSpan ModalMaxWait = TimeSpan.FromMinutes(5);

        /// <summary>Queue a UI-thread modal. The task must complete only after the modal closes.</summary>
        public static void EnqueueModal(string key, int priority, Func<Task> show, Action? onAbandoned = null)
        {
            if (string.IsNullOrWhiteSpace(key) || show == null) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => EnqueueModal(key, priority, show, onAbandoned));
                return;
            }
            if (!_modalQueue.Enqueue(key, priority)) return;
            _modalShows[key] = (show, onAbandoned);
            if (_modalPumpRunning) return;
            _modalPumpRunning = true;
            var token = _modalCancellation.Token;
            Dispatcher.UIThread.Post(() => _ = PumpModalsAsync(token), DispatcherPriority.Normal);
        }

        private static bool CanStartModal() => StartupQueueCore.CanStartModal(
            Views.Windows.MainShellWindow.IsStartupDialogShowing, AppUpdater.IsUpdateDialogActive,
            CoreTutorial.IsActive, ModalWindowReady(), StartupQueueCore.IsParked(World(UtcNow)));

        private static async Task PumpModalsAsync(CancellationToken token)
        {
            try
            {
                while (!_modalQueue.IsEmpty)
                {
                    var next = _modalQueue.Next();
                    if (next == null) break;
                    var key = next.Value.Key;
                    await Task.Delay(ModalSettle, Time, token);
                    var waited = TimeSpan.Zero;
                    while (!CanStartModal() && waited < ModalMaxWait)
                    {
                        await Task.Delay(ModalPoll, Time, token);
                        if (!StartupQueueCore.IsParked(World(UtcNow))) waited += ModalPoll;
                    }
                    _modalQueue.Remove(key);
                    if (!_modalShows.Remove(key, out var surface)) continue;
                    if (!CanStartModal())
                    {
                        if (surface.Abandoned != null) StartupInbox.RunSafely(surface.Abandoned, key, "abandon");
                        continue;
                    }
                    _modalUp = true;
                    Views.Windows.MainShellWindow.IsStartupDialogShowing = true;
                    try { await surface.Show(); }
                    catch (Exception ex) { Log.Warning(ex, "[Startup] '{Key}' failed to show", key); }
                    finally
                    {
                        _modalUp = false;
                        Views.Windows.MainShellWindow.IsStartupDialogShowing = false;
                        // WPF FlushPendingRemoteMediaOffer: an offer parked behind this modal gets its turn.
                        RemoteMediaOffer.FlushPending();
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { Log.Warning(ex, "[Startup] modal pump failed"); }
            finally
            {
                // WPF StartupPresenter.cs:491 restarts the pump while entries remain, so a failed pump strands nothing.
                if (!token.IsCancellationRequested && !_modalQueue.IsEmpty)
                    Dispatcher.UIThread.Post(() => _ = PumpModalsAsync(token), DispatcherPriority.Normal);
                else if (!token.IsCancellationRequested)
                {
                    _modalPumpRunning = false;
                    // WPF StartupPresenter.cs:552 FlushDeferred: the drained ladder asks the held offers again.
                    if (!ModalPending)
                    {
                        var held = _held.ToArray();
                        _held.Clear();
                        foreach (var item in held) PresentOrInbox(item);
                    }
                }
            }
        }

        /// <summary>WPF's 1 s re-check of the held surfaces (Services/Startup/StartupPresenter.cs:541).</summary>
        internal static readonly TimeSpan PassiveRecheck = TimeSpan.FromSeconds(1);

        /// <summary>The ladder's clock, which also drives the held-surface re-check, so a test that steps it
        /// decides exactly when a held card opens and one that does not never gets a card mid-input (P08, P76).</summary>
        internal static TimeProvider Time = TimeProvider.System;
        private static DateTime UtcNow => Time.GetUtcNow().UtcDateTime;

        /// <summary>WPF IsQuiet: a passive surface offered now would become an Inbox row.</summary>
        public static bool IsQuiet =>
            StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow)) != StartupRouting.Present;

        /// <summary>WPF BeginFirstLaunchQuiet: called once from the far side of the first-run wizard.</summary>
        public static void BeginFirstLaunchQuiet(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return;
            var until = UtcNow + span;
            if (_firstLaunchUntilUtc is DateTime existing && existing >= until) return;
            _firstLaunchUntilUtc = until;
            Log.Information("[Startup] first-launch quiet window open for {Minutes:0.#} min", span.TotalMinutes);
        }

        internal static QuietInputs World(DateTime nowUtc) => new()
        {
            LauncherHolding = LauncherHolding,
            LadderBusy = ModalPending || _modalPumpRunning || !_modalQueue.IsEmpty,
            ModalUp = _modalUp || Views.Windows.MainShellWindow.IsStartupDialogShowing || AppUpdater.IsUpdateDialogActive,
            TutorialActive = CoreTutorial.IsActive,
            SessionRunning = CoreSession.IsSessionRunning,
            GameHostUp = Views.Games.GameWindow.IsAnyStartupGameOpen() || Views.Windows.IntakeHostWindow.IsAnyOpen(),
            FirstLaunchUntilUtc = _firstLaunchUntilUtc,
            NowUtc = nowUtc,
        };

        /// <summary>WPF PresentOrInbox: open now, or park as a row (one per key). Safe from any thread.</summary>
        public static void PresentOrInbox(InboxItem item)
        {
            if (item == null) return;
            var generation = PresentationGeneration;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation == PresentationGeneration) PresentOrInbox(item);
                    else Inbox.File(item);
                });
                return;
            }

            var route = StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow));
            // One row per key: an offer already held or filed is not held or filed a second time.
            if (route != StartupRouting.Present &&
                (Inbox.Contains(item.Key) || _held.Any(h => string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase)))) return;
            if (route == StartupRouting.Defer)
            {
                _held.Add(item);
                // WPF StartupPresenter.cs:247: modal deferrals wait for the drain, without a timer.
                return;
            }
            if (route == StartupRouting.Present)
            {
                // One passive surface on screen at a time: the next one waits for it to close.
                if (StartupQueueCore.PassiveWaits(_lastPassiveUtc, UtcNow, PassiveWindowUp()))
                {
                    if (!_held.Any(h => string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase))) _held.Add(item);
                    Log.Debug("[Startup] '{Key}' waits for the passive surface already up", item.Key);
                    EnsurePassiveWatch();
                    return;
                }
                _lastPassiveUtc = UtcNow;
                StartupInbox.RunSafely(item.Open, item.Key, "open");
                return;
            }
            if (Inbox.File(item))
                Log.Information("[Startup] '{Key}' went to the Inbox ({Count} waiting)", item.Key, Inbox.UnreadCount);
        }

        // Passive windows open right now. Tracked by class handler rather than read from the
        // desktop lifetime, so it holds for any host (and headless tests, which have none).
        private static readonly HashSet<Window> _passiveUp = new();

        static StartupLadder()
        {
            Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is IPassiveStartupSurface) _passiveUp.Add(w); });
            Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => _passiveUp.Remove(w));
        }

        /// <summary>A feature card or an announcement is on screen.</summary>
        internal static bool PassiveWindowUp() => _passiveUp.Any(w => w.IsVisible);

        /// <summary>Re-asks the held surfaces once the passive one on screen has closed.</summary>
        private static void EnsurePassiveWatch()
        {
            if (_passiveTimer != null) return;
            // WPF cadence and priority (StartupPresenter.cs:541, Normal: Background work starves); a stepped clock fires
            // it on the stepping (UI) thread, the system clock on the pool, which posts back.
            var generation = PresentationGeneration;
            void Tick() { if (generation == PresentationGeneration) PumpHeld(); }
            _passiveTimer = Time.CreateTimer(_ =>
            {
                if (Dispatcher.UIThread.CheckAccess()) Tick();
                else Dispatcher.UIThread.Post(Tick, DispatcherPriority.Normal);
            }, null, PassiveRecheck, PassiveRecheck);
        }

        /// <summary>One tick of the watch: re-routes the held surfaces once nothing passive is up.</summary>
        internal static void PumpHeld()
        {
            if (ModalPending || StartupQueueCore.PassiveWaits(_lastPassiveUtc, UtcNow, PassiveWindowUp())) return;
            _passiveTimer?.Dispose();
            _passiveTimer = null;
            var held = _held.ToList();
            _held.Clear();
            foreach (var h in held) PresentOrInbox(h);
        }

        // WPF StartupPresenter.cs:247,552 retains deferred offers. Panic parks them in the Inbox,
        // dispose the passive watch and invalidate callbacks already posted to the dispatcher.
        internal static void CancelDeferred()
        {
            PresentationGeneration++;
            _passiveTimer?.Dispose();
            _passiveTimer = null;
            var held = _held.ToArray();
            _held.Clear();
            foreach (var item in held) Inbox.File(item); // never run Open or Dismiss on panic
        }

        /// <summary>Test seam: forget the quiet window, the passive pacing, the passive windows up and every row.</summary>
        internal static void ResetForTests()
        {
            LauncherHolding = false;
            ModalPending = false;
            _modalCancellation.Cancel();
            _modalCancellation = new CancellationTokenSource();
            _modalQueue.Clear();
            _modalShows.Clear();
            _modalPumpRunning = _modalUp = false;
            ModalWindowReady = () => Views.Windows.MainShellWindow.Current?.IsVisible == true;
            _firstLaunchUntilUtc = null;
            _lastPassiveUtc = null;
            CancelDeferred();
            _passiveUp.Clear();   // a card another test left open must not hold this test's surface
            // A held clock, not the system one: a held card opens only when a test steps it (P76).
            Time = new TestClock();
            Inbox.Items.Clear();
        }

        /// <summary>Test clock: stands still until <see cref="Advance"/>, which fires the timers that fall due.</summary>
        internal sealed class TestClock : TimeProvider
        {
            public DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
            public readonly List<Timer> Timers = new();
            public override DateTimeOffset GetUtcNow() => Now;
            public override long GetTimestamp() => Now.UtcTicks;          // stepped too, so GetElapsedTime follows Advance
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var t = new Timer(this, () => callback(state), Now + dueTime, period);
                Timers.Add(t);
                return t;
            }

            public void Advance(TimeSpan by)
            {
                var end = Now + by;
                while (Timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } t)
                {
                    Now = t.Due;
                    if (t.Period > TimeSpan.Zero) t.Due += t.Period; else Timers.Remove(t);
                    t.Fire();
                }
                Now = end;
            }

            internal sealed class Timer(TestClock clock, Action fire, DateTimeOffset due, TimeSpan period) : ITimer
            {
                public readonly Action Fire = fire;
                public DateTimeOffset Due = due;
                public readonly TimeSpan Period = period;
                public bool Change(TimeSpan dueTime, TimeSpan p) => false;
                public void Dispose() => clock.Timers.Remove(this);
                public ValueTask DisposeAsync() { Dispose(); return default; }
            }
        }
    }
}
