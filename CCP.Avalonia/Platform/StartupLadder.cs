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
    /// </summary>
    internal static class StartupLadder
    {
        public static StartupInbox Inbox { get; } = new();

        private static DateTime? _firstLaunchUntilUtc;
        private static DateTime? _lastPassiveUtc;
        private static readonly List<InboxItem> _held = new();
        private static ITimer? _passiveTimer;
        private static ITimer? _quietTimer;
        private static bool _lastQuiet;
        private static bool _modalUp;
        private static bool _pumpRunning;
        private static int _generation;
        private static readonly StartupQueueCore Queue = new();
        private static readonly Dictionary<string, (Window Owner, Func<Task> Show, Action? Abandoned)> Shows =
            new(StringComparer.OrdinalIgnoreCase);
        private static ITimer? _modalTimer;
        public static bool Held { get; private set; }
        public static bool IsLadderIdle => !_pumpRunning && !_modalUp && Queue.IsEmpty;
        public static event Action? Drained;
        public static event Action? QuietChanged;

        public static void Hold() { Held = true; NotifyQuietMaybeChanged(); }
        public static void Release() { Held = false; NotifyQuietMaybeChanged(); }

        // WPF StartupPresenter: 750 ms settle, 500 ms clearance poll, five minutes of
        // unparked waiting per EXACT key. Avalonia modals complete asynchronously.
        public static void EnqueueModal(string key, int priority, Window owner, Func<Task> show, Action? abandoned = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            { Dispatcher.UIThread.Post(() => EnqueueModal(key, priority, owner, show, abandoned)); return; }
            if (!Queue.Enqueue(key, priority)) return;
            Shows[key] = (owner, show, abandoned);
            StartPump();
        }

        private static void StartPump()
        {
            if (_pumpRunning) return;
            _pumpRunning = true;
            NotifyQuietMaybeChanged();
            var generation = _generation;
            Dispatcher.UIThread.Post(() => { if (generation == _generation) StartNext(); }, DispatcherPriority.Normal);
        }

        private static void StartNext()
        {
            if (Queue.Next() is not { } next)
            {
                _pumpRunning = false;
                FlushHeld();
                NotifyQuietMaybeChanged();
                StartupInbox.RunSafely(() => Drained?.Invoke(), "ladder", "drain");
                return;
            }
            ScheduleModal(TimeSpan.FromMilliseconds(750), () => PollModal(next.Key, TimeSpan.Zero));
        }

        private static void ScheduleModal(TimeSpan delay, Action action)
        {
            var generation = _generation;
            _modalTimer = Time.CreateTimer(_ =>
            {
                void Tick()
                {
                    if (generation != _generation) return;
                    _modalTimer?.Dispose();
                    _modalTimer = null;
                    action();
                }
                if (Dispatcher.UIThread.CheckAccess()) Tick(); else Dispatcher.UIThread.Post(Tick);
            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        private static void PollModal(string key, TimeSpan waited)
        {
            var surface = Shows[key];
            var world = World(UtcNow);
            bool parked = StartupQueueCore.IsParked(world);
            bool ready = StartupQueueCore.CanStartModal(
                _modalUp || Views.Windows.MainShellWindow.IsStartupDialogShowing,
                AppUpdater.IsUpdateDialogActive, world.TutorialActive, surface.Owner.IsVisible, parked);
            if (!ready && waited < TimeSpan.FromMinutes(5))
            {
                ScheduleModal(TimeSpan.FromMilliseconds(500), () => PollModal(key,
                    waited + (StartupQueueCore.IsParked(World(UtcNow)) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(500))));
                return;
            }
            Queue.Remove(key);
            Shows.Remove(key);
            if (!ready)
            {
                if (surface.Abandoned != null) StartupInbox.RunSafely(surface.Abandoned, key, "abandon");
                StartNext();
                return;
            }
            _ = RunModalAsync(key, surface.Show);
        }

        private static async Task RunModalAsync(string key, Func<Task> show)
        {
            var generation = _generation;
            _modalUp = Views.Windows.MainShellWindow.IsStartupDialogShowing = true;
            NotifyQuietMaybeChanged();
            try { await show(); }
            catch (Exception ex) { Log.Warning(ex, "[Startup] '{Key}' failed to show", key); }
            finally
            {
                if (generation == _generation)
                {
                    _modalUp = Views.Windows.MainShellWindow.IsStartupDialogShowing = false;
                    NotifyQuietMaybeChanged();
                    StartNext();
                }
            }
        }

        private static void NotifyQuietMaybeChanged()
        {
            var quiet = IsQuiet;
            if (quiet && _quietTimer == null)
                _quietTimer = Time.CreateTimer(_ =>
                {
                    if (Dispatcher.UIThread.CheckAccess()) NotifyQuietMaybeChanged();
                    else Dispatcher.UIThread.Post(NotifyQuietMaybeChanged);
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            if (!quiet) { _quietTimer?.Dispose(); _quietTimer = null; }
            if (quiet == _lastQuiet) return;
            _lastQuiet = quiet;
            StartupInbox.RunSafely(() => QuietChanged?.Invoke(), "ladder", "quiet changed");
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
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => BeginFirstLaunchQuiet(span)); return; }
            var until = UtcNow + span;
            if (_firstLaunchUntilUtc is DateTime existing && existing >= until) return;
            _firstLaunchUntilUtc = until;
            NotifyQuietMaybeChanged();
            Log.Information("[Startup] first-launch quiet window open for {Minutes:0.#} min", span.TotalMinutes);
        }

        internal static QuietInputs World(DateTime nowUtc) => new()
        {
            LadderBusy = !IsLadderIdle,
            LauncherHolding = Held,
            // WPF SafeGameHostUp (StartupPresenter.cs:373): games only, never the For You feed.
            GameHostUp = Views.Windows.PanicSurfaces.AnyOwnsTheScreen(skipId: "games") || Views.Games.GameWindow.IsAnyGameOpen(),
            ModalUp = _modalUp || Views.Windows.MainShellWindow.IsStartupDialogShowing || AppUpdater.IsUpdateDialogActive,
            TutorialActive = CoreTutorial.IsActive,
            SessionRunning = CoreSession.IsSessionRunning,
            FirstLaunchUntilUtc = _firstLaunchUntilUtc,
            NowUtc = nowUtc,
        };

        /// <summary>WPF PresentOrInbox: open now, or park as a row (one per key). Safe from any thread.</summary>
        public static void PresentOrInbox(InboxItem item)
        {
            if (item == null) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => PresentOrInbox(item)); return; }

            var routing = StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow));
            if (routing == StartupRouting.Present)
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
            if (Inbox.Contains(item.Key) || _held.Any(h => string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase))) return;
            if (routing == StartupRouting.Defer) { _held.Add(item); return; }
            NotifyQuietMaybeChanged();
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
            _passiveTimer = Time.CreateTimer(_ =>
            {
                if (Dispatcher.UIThread.CheckAccess()) PumpHeld();
                else Dispatcher.UIThread.Post(PumpHeld, DispatcherPriority.Normal);
            }, null, PassiveRecheck, PassiveRecheck);
        }

        /// <summary>One tick of the watch: re-routes the held surfaces once nothing passive is up.</summary>
        internal static void PumpHeld()
        {
            if (StartupQueueCore.PassiveWaits(_lastPassiveUtc, UtcNow, PassiveWindowUp())) return;
            _passiveTimer?.Dispose();
            _passiveTimer = null;
            FlushHeld();
        }

        private static void FlushHeld()
        {
            var held = _held.ToList();
            _held.Clear();
            foreach (var h in held) PresentOrInbox(h);
        }

        /// <summary>Test seam: forget the quiet window, the passive pacing, the passive windows up and every row.</summary>
        internal static void ResetForTests()
        {
            _generation++;
            _modalTimer?.Dispose(); _modalTimer = null;
            _quietTimer?.Dispose(); _quietTimer = null;
            // Only hand back the flag the ladder raised itself: a test (or the wizard) may own it.
            if (_modalUp) Views.Windows.MainShellWindow.IsStartupDialogShowing = false;
            _lastQuiet = _modalUp = _pumpRunning = Held = false;
            Queue.Clear(); Shows.Clear();
            Drained = null; QuietChanged = null;
            _firstLaunchUntilUtc = null;
            _lastPassiveUtc = null;
            _held.Clear();
            _passiveUp.Clear();   // a card another test left open must not hold this test's surface
            _passiveTimer?.Dispose();
            _passiveTimer = null;
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
