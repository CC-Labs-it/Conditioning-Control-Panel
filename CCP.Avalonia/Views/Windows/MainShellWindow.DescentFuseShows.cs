using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private DescentCountdownService? _showCountdown;
    private DispatcherTimer? _fuseRetryTimer;
    private int _fuseRetryAttempts, _fuseShowGeneration;
    private bool _catchUpQueued;
    private DateTime? _handledLiveZeroAt;

    internal bool FuseRetryRunning => _fuseRetryTimer != null;
    internal int FuseRetryAttempts => _fuseRetryAttempts;

    // WPF DescentShowDirector.Arm: the service has settled ShouldPlayCatchUp before the shell exists.
    // ponytail: no ceremony holds are needed here: WPF retired that window, and this head never opens it.
    private void InitializeFuseShows(DescentCountdownService fuse)
    {
        _showCountdown = fuse;
        fuse.ZeroReached += OnFuseZeroReached;
        fuse.ZeroObservedLate += OnFuseZeroObservedLate;
        if (fuse.ShouldPlayCatchUp) QueueFuseCatchUp();
        else if (!fuse.ZeroPassedWhileAway && fuse.LastAnnouncedPhase == DescentFusePhase.Zero)
        {
            // The countdown starts before async account startup. Its live event can precede the shell.
            // Recover only an ANNOUNCED zero, and deduplicate it against an event arriving before this post.
            var generation = _fuseShowGeneration;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == _fuseShowGeneration && !fuse.ZeroPassedWhileAway
                    && fuse.LastAnnouncedPhase == DescentFusePhase.Zero)
                    OnFuseZeroReached(fuse, EventArgs.Empty);
            }, DispatcherPriority.Normal);
        }
        Closed += (_, _) =>
        {
            fuse.ZeroReached -= OnFuseZeroReached;
            fuse.ZeroObservedLate -= OnFuseZeroObservedLate;
            CancelQueuedFuseShow();
            StopFuseRetry();
        };
    }

    private void OnFuseZeroReached(object? sender, EventArgs e)
    {
        if (_windowClosed || _showCountdown?.CeremonyAtUtc is not { } instant || _handledLiveZeroAt == instant) return;
        _handledLiveZeroAt = instant; // a postponed instant remains eligible for its own live zero
        if (OpenLiveFuseShow() is { } show)
            show.Closed += (_, _) => BeginFuseRetry();
    }

    private void OnFuseZeroObservedLate(object? sender, EventArgs e)
    {
        if (_windowClosed) return;
        if (_showCountdown?.ShouldPlayCatchUp == true) QueueFuseCatchUp();
        else BeginFuseRetry();
    }

    private void QueueFuseCatchUp()
    {
        if (_windowClosed || _catchUpQueued) return;
        _catchUpQueued = true;
        var generation = _fuseShowGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            _catchUpQueued = false;
            if (_windowClosed || generation != _fuseShowGeneration) return;
            var show = DescentFuseWindow.Open(DescentShowKind.CatchUp);
            if (show == null) return; // WPF: no window played, so the next launch still owes the crack.
            show.Closed += (_, _) =>
            {
                _showCountdown?.MarkCatchUpCrackPlayed(); // WPF: mark on EVERY exit, including panic.
                BeginFuseRetry();
            };
        }, DispatcherPriority.Normal);
    }

    // The registered panic stop cancels an opener still queued behind startup work as well as a live show.
    internal void CancelQueuedFuseShow() => _fuseShowGeneration++;

    private bool FuseRetryShouldContinue() => DescentPostZeroRetry.ShouldContinue(
        _fuseRetryAttempts,
        fuseArmed: _showCountdown?.CeremonyAtUtc != null,
        ceremonyOpen: false, // retired on both heads (WPF IsCeremonyOpen is constant false)
        offerInHand: false,  // SyncPush applies the silent migration acknowledgment immediately
        migrationCompleted: CoreSettings.Current.DescentMigrationCompleted,
        choicePending: DescentMigrationChoices.IsValid(CoreSettings.Current.PendingDescentMigrationChoice));

    private void BeginFuseRetry()
    {
        if (_windowClosed || _fuseRetryTimer != null || !FuseRetryShouldContinue()) return;
        _fuseRetryAttempts = 0;
        _fuseRetryTimer = new DispatcherTimer { Interval = DescentPostZeroRetry.Every };
        _fuseRetryTimer.Tick += (_, _) => TickFuseRetry();
        _fuseRetryTimer.Start();
    }

    // One real timer tick, exposed internally so the bounded minute cadence needs no wall-clock test wait.
    internal void TickFuseRetry()
    {
        if (_fuseRetryTimer == null) return;
        if (_windowClosed || !FuseRetryShouldContinue()) { StopFuseRetry(); return; }
        _fuseRetryAttempts++;
        _ = RequestFuseSync();
    }

    private void StopFuseRetry()
    {
        _fuseRetryTimer?.Stop();
        _fuseRetryTimer = null;
    }

    // WPF RequestSync/OnRetryTick: the existing sync owns cooldown, offline refusal and acknowledgment.
    internal static Task<bool> RequestFuseSync()
    {
        try { return Platform.AccountSeed.Sync?.PushAsync("descent-fuse") ?? Task.FromResult(false); }
        catch (Exception ex) { Log.Debug("[Fuse] Sync could not start: {Error}", ex.Message); return Task.FromResult(false); }
    }
}
