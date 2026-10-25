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
    /// Modal ordering uses Core StartupQueueCore; callbacks complete only after their dialog closes.
    /// </summary>
    internal static class StartupLadder
    {
        private static readonly StartupQueueCore Queue = new();
        private static readonly Dictionary<string, (Func<Window, Task> Show, Action? Abandoned)> Shows = new(StringComparer.OrdinalIgnoreCase);
        private static ITimer? _modalTimer;
        private static bool _modalUp;
        private static string? _waitingKey;
        private static DateTime _checkAt;
        private static TimeSpan _waited;
        private static int _generation;
        internal static Func<Window?> Owner = () => Views.Windows.MainShellWindow.Current;
        public static bool IsLadderIdle => Queue.IsEmpty && !_modalUp;

        public static void EnqueueModal(string key, int priority, Func<Window, Task> show, Action? onAbandoned = null)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => EnqueueModal(key, priority, show, onAbandoned)); return; }
            if (!Queue.Enqueue(key, priority)) return;
            Shows[key] = (show, onAbandoned);
            _modalTimer ??= Time.CreateTimer(_ =>
            {
                if (Dispatcher.UIThread.CheckAccess()) PumpModal();
                else Dispatcher.UIThread.Post(PumpModal);
            }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        }

        // WPF StartupPresenter: 750 ms settle, 500 ms polling, five unparked minutes per key.
        internal static async void PumpModal()
        {
            if (_modalUp) return;
            if (_waitingKey == null)
            {
                if (Queue.Next() is not { } next) { FinishModalPump(); return; }
                _waitingKey = next.Key;
                _waited = TimeSpan.Zero;
                _checkAt = UtcNow + TimeSpan.FromMilliseconds(750);
                return;
            }
            if (UtcNow < _checkAt) return;
            var owner = Owner();
            var world = World(UtcNow);
            bool parked = StartupQueueCore.IsParked(world);
            bool ready = StartupQueueCore.CanStartModal(false, AppUpdater.IsUpdateDialogActive || Views.Windows.MainShellWindow.IsStartupDialogShowing,
                CoreTutorial.IsActive, owner is { IsVisible: true, WindowState: not WindowState.Minimized }, parked);
            if (!ready && _waited < TimeSpan.FromMinutes(5))
            {
                if (!parked) _waited += TimeSpan.FromMilliseconds(500);
                _checkAt = UtcNow + TimeSpan.FromMilliseconds(500);
                return;
            }
            var key = _waitingKey;
            _waitingKey = null;
            Queue.Remove(key);
            if (!Shows.Remove(key, out var surface)) return;
            if (!ready) { StartupInbox.RunSafely(surface.Abandoned, key, "abandon"); return; }
            _modalUp = true;
            var generation = _generation;
            Views.Windows.MainShellWindow.IsStartupDialogShowing = true;
            try { await surface.Show(owner!); }
            catch (Exception ex) { Log.Warning(ex, "[Startup] modal {Key} failed", key); }
            finally
            {
                if (generation == _generation)
                {
                    _modalUp = false;
                    Views.Windows.MainShellWindow.IsStartupDialogShowing = false;
                    if (Queue.IsEmpty) FinishModalPump();
                }
            }
        }

        private static void FinishModalPump()
        {
            _modalTimer?.Dispose();
            _modalTimer = null;
            PumpHeld();
        }

        internal static void StopForPanic()
        {
            _generation++;
            Queue.Clear();
            foreach (var pair in Shows) StartupInbox.RunSafely(pair.Value.Abandoned, pair.Key, "abandon");
            Shows.Clear();
            _waitingKey = null;
            _modalUp = false;
            Views.Windows.MainShellWindow.IsStartupDialogShowing = false;
            _modalTimer?.Dispose();
            _modalTimer = null;
            RequiredNameAsk.CloseForPanic();
        }

        public static StartupInbox Inbox { get; } = new();

        private static DateTime? _firstLaunchUntilUtc;
        private static DateTime? _lastPassiveUtc;
        private static readonly List<InboxItem> _held = new();
        private static ITimer? _passiveTimer;

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
            LadderBusy = !IsLadderIdle,
            GameHostUp = Views.Games.GameWindow.ActiveOpen() != null,
            LauncherHolding = Views.Windows.LauncherWindow.Instance is { IsVisible: true } && Owner() is not { IsVisible: true },
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

            var route = StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow));
            if (route == StartupRouting.Defer)
            {
                if (!_held.Any(h => string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase))) _held.Add(item);
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
            var held = _held.ToList();
            _held.Clear();
            foreach (var h in held) PresentOrInbox(h);
        }

        /// <summary>Test seam: forget the quiet window, the passive pacing, the passive windows up and every row.</summary>
        internal static void ResetForTests()
        {
            StopForPanic();
            Owner = () => Views.Windows.MainShellWindow.Current;
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
