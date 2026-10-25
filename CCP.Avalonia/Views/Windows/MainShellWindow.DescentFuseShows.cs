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
    private int _fuseRetryAttempts;

    internal bool FuseRetryRunning => _fuseRetryTimer != null;
    internal int FuseRetryAttempts => _fuseRetryAttempts;

    // WPF DescentShowDirector with DescentFuseWindow.ShowsRetired: the Live and CatchUp shows refuse
    // to open, so a startup catch-up and a live zero do nothing (no mark, no retry). The only path
    // that still acts is the late zero with no crack owed (DescentShowDirector.cs:192-196).
    private void InitializeFuseShows(DescentCountdownService fuse)
    {
        _showCountdown = fuse;
        fuse.ZeroReached += OnFuseZeroReached;
        fuse.ZeroObservedLate += OnFuseZeroObservedLate;
        Closed += (_, _) =>
        {
            fuse.ZeroReached -= OnFuseZeroReached;
            fuse.ZeroObservedLate -= OnFuseZeroObservedLate;
            StopFuseRetry();
        };
    }

    // WPF OnZeroReached: Open(Live) returns null, so no Closed handler and no retry.
    private void OnFuseZeroReached(object? sender, EventArgs e)
    {
        if (!_windowClosed) _ = OpenLiveFuseShow();
    }

    // WPF OnZeroObservedLate: an owed catch-up goes to the retired opener (nothing plays, nothing is
    // marked); otherwise the post-zero retry makes sure the sync conversation happens.
    private void OnFuseZeroObservedLate(object? sender, EventArgs e)
    {
        if (_windowClosed) return;
        if (_showCountdown?.ShouldPlayCatchUp == true) _ = DescentFuseWindow.Open(DescentShowKind.CatchUp);
        else BeginFuseRetry();
    }

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
    // Returns the sync it started so a test can await it instead of polling.
    internal Task TickFuseRetry()
    {
        if (_fuseRetryTimer == null) return Task.CompletedTask;
        if (_windowClosed || !FuseRetryShouldContinue()) { StopFuseRetry(); return Task.CompletedTask; }
        _fuseRetryAttempts++;
        return RequestFuseSync();
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
