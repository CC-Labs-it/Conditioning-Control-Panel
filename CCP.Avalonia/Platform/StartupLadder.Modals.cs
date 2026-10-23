using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static partial class StartupLadder
{
    private static readonly StartupQueueCore _modals = new();
    private static readonly Dictionary<string, (Window Owner, Func<Task> Show)> _shows = new(StringComparer.OrdinalIgnoreCase);
    private static CancellationTokenSource _modalStop = new();
    private static bool _pumpRunning;

    /// <summary>WPF EnqueueModal/RunModal: ordered, deduplicated turns; await the dialog before releasing its flag.</summary>
    internal static void EnqueueModal(string key, int priority, Window owner, Func<Task> show)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => EnqueueModal(key, priority, owner, show)); return; }
        if (!_modals.Enqueue(key, priority)) return;
        _shows[key] = (owner, show);
        if (_pumpRunning) return;
        _pumpRunning = true;
        var token = _modalStop.Token;
        Dispatcher.UIThread.Post(async () => await PumpModalsAsync(token), DispatcherPriority.Normal);
    }

    private static async Task PumpModalsAsync(CancellationToken token)
    {
        try
        {
            while (_modals.Next() is { } next)
            {
                // WPF StartupPresenter: 750ms settle, 500ms polls, five minutes per exact queued key.
                await Task.Delay(TimeSpan.FromMilliseconds(750), Time, token);
                if (!_shows.TryGetValue(next.Key, out var surface)) continue;
                bool Ready() => StartupQueueCore.CanStartModal(MainShellWindow.IsStartupDialogShowing,
                    AppUpdater.IsUpdateDialogActive, CoreTutorial.IsActive,
                    surface.Owner.IsVisible && surface.Owner.WindowState != WindowState.Minimized,
                    StartupQueueCore.IsParked(World(UtcNow)));
                var waited = TimeSpan.Zero;
                while (!Ready() && waited < TimeSpan.FromMinutes(5))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), Time, token);
                    if (!StartupQueueCore.IsParked(World(UtcNow))) waited += TimeSpan.FromMilliseconds(500);
                }
                _modals.Remove(next.Key);
                _shows.Remove(next.Key);
                if (!Ready()) continue;   // unspent; retry on the next launch
                MainShellWindow.IsStartupDialogShowing = true;
                try { await surface.Show(); }
                catch (Exception ex) { Log.Warning(ex, "Startup surface {Key} failed", next.Key); }
                finally { MainShellWindow.IsStartupDialogShowing = false; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (!token.IsCancellationRequested) { _pumpRunning = false; PumpHeld(); }
        }
    }

    internal static void CancelModals()
    {
        _modalStop.Cancel();
        _modalStop.Dispose();
        _modalStop = new();
        _modals.Clear();
        _shows.Clear();
        _pumpRunning = false;
    }
}
