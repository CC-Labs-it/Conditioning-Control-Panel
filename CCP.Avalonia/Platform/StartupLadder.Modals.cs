using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static partial class StartupLadder
{
    // WPF StartupPresenter: 750 ms settle, 500 ms polls, five minutes excluding parked time.
    private static readonly StartupQueueCore Queue = new();
    private static readonly Dictionary<string, (Func<Window?, Task> Show, Action? Abandoned)> Shows = new(StringComparer.OrdinalIgnoreCase);
    private static ITimer? _modalTimer;
    private static bool _pumpRunning, _modalUp;
    private static int _generation;
    internal static Func<Window?> ModalOwner = () => MainShellWindow.Current;
    internal static Func<bool> WindowReady = () => ModalOwner() is { IsVisible: true };
    // Weak: a static strong capture kept the last closed shell alive (ShellMemoryTests).
    internal static void Attach(MainShellWindow owner)
    {
        var weak = new WeakReference<MainShellWindow>(owner);
        ModalOwner = () => weak.TryGetTarget(out var shell) ? shell : null;
    }
    public static bool IsModalUp => _modalUp;
    public static bool IsLadderIdle => !_pumpRunning && !_modalUp && Queue.IsEmpty;
    public static event Action? Drained;

    public static void EnqueueModal(string key, int priority, Func<Window?, Task> show, Action? onAbandoned = null)
    {
        if (string.IsNullOrWhiteSpace(key) || show == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        { Dispatcher.UIThread.Post(() => EnqueueModal(key, priority, show, onAbandoned)); return; }
        if (!Queue.Enqueue(key, priority)) return;
        Shows[key] = (show, onAbandoned);
        if (_pumpRunning) return;
        _pumpRunning = true;
        var generation = _generation;
        // Choose the first turn after other startup offers have been posted, at Normal priority.
        Dispatcher.UIThread.Post(() => { if (generation == _generation) NextModal(); }, DispatcherPriority.Normal);
    }

    private static void ScheduleModal(TimeSpan delay, Action action)
    {
        _modalTimer?.Dispose();
        var generation = _generation;
        _modalTimer = Time.CreateTimer(_ => OnUI(() =>
        {
            if (generation != _generation) return;
            _modalTimer?.Dispose();
            _modalTimer = null;
            action();
        }), null, delay, Timeout.InfiniteTimeSpan);
    }

    private static void NextModal()
    {
        if (Queue.Next() is { } next)
        {
            NotifyQuietMaybeChanged();
            ScheduleModal(TimeSpan.FromMilliseconds(750), () => WaitForScreen(next.Key, TimeSpan.Zero));
            return;
        }
        _pumpRunning = false;
        FlushHeld(); // Re-route before Drained listeners can enqueue another modal.
        NotifyQuietMaybeChanged();
        try { Drained?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "[Startup] a Drained handler threw"); }
    }

    private static void WaitForScreen(string key, TimeSpan waited)
    {
        var world = World(UtcNow);
        var clear = StartupQueueCore.CanStartModal(world.ModalUp, AppUpdater.IsUpdateDialogActive,
            world.TutorialActive, WindowReady(), StartupQueueCore.IsParked(world));
        if (!clear && waited < TimeSpan.FromMinutes(5))
        {
            ScheduleModal(TimeSpan.FromMilliseconds(500), () => WaitForScreen(key,
                waited + (StartupQueueCore.IsParked(World(UtcNow)) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(500))));
            return;
        }
        // Keep the exact key selected before the wait: a new higher priority gets its own budget.
        if (!Queue.Remove(key) || !Shows.Remove(key, out var surface)) { NextModal(); return; }
        if (!clear)
        {
            if (surface.Abandoned != null) StartupInbox.RunSafely(surface.Abandoned, key, "abandon");
            NextModal();
            return;
        }
        _ = RunModalAsync(key, surface.Show);
    }

    private static async Task RunModalAsync(string key, Func<Window?, Task> show)
    {
        var generation = _generation;
        _modalUp = MainShellWindow.IsStartupDialogShowing = true;
        NotifyQuietMaybeChanged();
        try { await show(ModalOwner()); }
        catch (Exception ex) { Log.Warning(ex, "[Startup] '{Key}' failed to show", key); }
        finally
        {
            if (generation == _generation)
            {
                _modalUp = MainShellWindow.IsStartupDialogShowing = false;
                NotifyQuietMaybeChanged();
                NextModal();
            }
        }
    }

    private static void ResetModalsForTests()
    {
        _generation++;
        _modalTimer?.Dispose();
        _modalTimer = null;
        Queue.Clear();
        Shows.Clear();
        // Only clear the shell flag the ladder itself raised: tests set it to stand in for a wizard.
        if (_modalUp) MainShellWindow.IsStartupDialogShowing = false;
        _pumpRunning = _modalUp = false;
        Drained = null;
        ModalOwner = () => MainShellWindow.Current;
        WindowReady = () => ModalOwner() is { IsVisible: true };
    }
}
