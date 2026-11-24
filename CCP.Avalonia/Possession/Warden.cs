// WPF Services/Possession/Warden.cs:25,139,188,229,298: the companion's four possession verbs.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class Warden : IPossessionWarden, IDisposable
{
    private readonly LockdownService _lockdown;
    private CancellationTokenSource? _active, _leaving;
    private readonly object _leaveGate = new();
    private DateTime _lastAppearance = DateTime.MinValue, _lastStare = DateTime.MinValue;
    private bool _busy, _hasLeft, _stopped, _returning;
    private int _epoch;
    private AvatarTubeWindow? _ownedTube;

    internal Warden(LockdownService lockdown)
    {
        _lockdown = lockdown;
        lockdown.LockdownActivated += Reset;
    }

    public bool IsAvailable => !_stopped && !_busy && CoreSettings.Current.LockdownWardenEnabled
        && AvatarTubeWindow.Live is { CanPerformPossessionMove: true }
        && CoreEngine.Video?.IsPlaying != true && WardenRules.CanAppear(DateTime.UtcNow, _lastAppearance);

    private CancellationTokenSource? Begin(CancellationToken ct)
    {
        if (_stopped || _busy || AvatarTubeWindow.Live is not { CanPerformPossessionMove: true } tube) return null;
        _busy = true;
        _ownedTube = tube;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(WardenRules.VerbTimeout);
        _active = cts;
        return cts;
    }

    private void End(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_active, cts)) { _active = null; _busy = false; }
        cts.Dispose();
    }

    public async Task KnockAsync(PossessionTarget target, CancellationToken ct)
    {
        if (target.Visual is not Control c || !c.IsEffectivelyVisible || !c.IsAttachedToVisualTree() || c.Bounds.Width <= 0 || c.Bounds.Height <= 0) return;
        var cts = Begin(ct); if (cts == null) return;
        var tube = _ownedTube!;
        try
        {
            var a = c.PointToScreen(default); var b = c.PointToScreen(new Point(c.Bounds.Width, c.Bounds.Height));
            var rect = new PixelRect(a, new PixelSize(b.X - a.X, b.Y - a.Y));
            _lastAppearance = DateTime.UtcNow;
            await tube.GlideToScreenRectAsync(rect, cts.Token);
            await Task.Delay(WardenRules.KnockBeatMs, cts.Token);
            var here = tube.Position;
            var dx = (int)WardenRules.KnockShove(rect.X + rect.Width / 2.0, here.X + PixelSize.FromSize(tube.Bounds.Size, tube.DesktopScaling).Width / 2.0);
            for (int i = 0; i < 2; i++)
            {
                await tube.GlideToScreenPointAsync(new PixelPoint(here.X + dx, here.Y), cts.Token);
                await tube.GlideToScreenPointAsync(here, cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();
            Bark(WardenRules.Knock);
            _ = GoHomeLater(cts.Token, _epoch);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Warden knock failed: {Error}", ex.Message); }
        finally { End(cts); }
    }

    private async Task GoHomeLater(CancellationToken ct, int epoch)
    {
        try { await Task.Delay(WardenRules.GoHomeDelayMs, ct); if (!_stopped && epoch == _epoch) await ReturnAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug("Warden delayed return failed: {Error}", ex.Message); }
    }

    public async Task StareAsync(string reason, CancellationToken ct)
    {
        if (!WardenRules.CanStare(DateTime.UtcNow, _lastStare)) return;
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null || !window.IsVisible) return;
        var cts = Begin(ct); if (cts == null) return;
        var tube = _ownedTube!;
        try
        {
            var a = window.PointToScreen(default); var b = window.PointToScreen(new Point(window.Bounds.Width, window.Bounds.Height));
            var size = PixelSize.FromSize(tube.Bounds.Size, tube.DesktopScaling);
            var (x, y) = WardenRules.StareSpot(a.X, a.Y, b.X - a.X, b.Y - a.Y, size.Width, size.Height);
            _lastAppearance = _lastStare = DateTime.UtcNow;
            await tube.GlideToScreenPointAsync(new PixelPoint((int)x, (int)y), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (BarkHead.Engine == null) tube.GigglePriority(WardenRules.FallbackStareLine(reason), playSound: false, aiGenerated: false, mood: "possessive");
            else Bark(WardenRules.Stare);
            await Task.Delay(WardenRules.StareLingerMs, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Warden stare failed: {Error}", ex.Message); }
        finally { End(cts); }
        if (!_stopped) await ReturnAsync(ct);
    }

    public async Task LeaveAsync(CancellationToken ct)
    {
        CancellationTokenSource? cts;
        lock (_leaveGate)
        {
            if (_hasLeft) return;
            cts = Begin(ct); if (cts == null) return;
            _leaving = cts;
        }
        var tube = _ownedTube!;
        try
        {
            var screen = tube.Screens.ScreenFromPoint(tube.Position) ?? tube.Screens.Primary;
            if (screen == null) return;
            _lastAppearance = DateTime.UtcNow;
            await tube.GlideToScreenPointAsync(new PixelPoint(tube.Position.X, screen.Bounds.Bottom), cts.Token, false);
            lock (_leaveGate)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_leaving, cts)) return;
                _hasLeft = true;
                int i = Random.Shared.Next(WardenRules.NoteKeys.Length);
                tube.ShowPossessionNote(WardenRules.NoteText(i, Loc.Get(WardenRules.NoteKeys[i])));
            }
            cts.Token.ThrowIfCancellationRequested();
            Bark(WardenRules.Leave);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Warden leave failed: {Error}", ex.Message); }
        finally
        {
            lock (_leaveGate) { if (ReferenceEquals(_leaving, cts)) _leaving = null; End(cts); }
        }
    }

    public async Task ReturnAsync(CancellationToken ct)
    {
        if (_returning || _stopped) return;
        _active?.Cancel();
        var tube = _ownedTube;
        if (tube == null) return;
        tube.HidePossessionNote();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(WardenRules.VerbTimeout);
        _active = cts;
        _busy = _returning = true;
        var epoch = _epoch;
        try
        {
            await tube.ReturnHomeAsync(cts.Token);
            if (!cts.IsCancellationRequested && _hasLeft) Bark(WardenRules.Return);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug("Warden return failed: {Error}", ex.Message); }
        finally { if (ReferenceEquals(_active, cts)) { _active = null; _busy = false; } if (epoch == _epoch) { _hasLeft = false; _returning = false; } }
    }

    private static void Bark(string verb)
    {
        try { BarkHead.Engine?.Raise(PossessionBarkTriggers.Warden, c => c.Set("verb", verb)); }
        catch (Exception ex) { Serilog.Log.Debug("Warden bark failed: {Error}", ex.Message); }
    }

    // WPF Warden.cs:81-95: cancel a leave and clear per-lockdown state without undoing the room
    // or interrupting a return that already belongs to the previous reassembly.
    private void Reset()
    {
        lock (_leaveGate)
        {
            try { _leaving?.Cancel(); } catch (ObjectDisposedException) { }
            _leaving = null; _hasLeft = false;
        }
        _stopped = false;
        _lastAppearance = _lastStare = DateTime.MinValue;
        try { AvatarTubeWindow.Live?.HidePossessionNote(); }
        catch (Exception ex) { Serilog.Log.Debug("Warden reset note failed: {Error}", ex.Message); }
    }

    // A real engine restart may rearm the warden in the same Lockdown without resetting cooldowns.
    internal void ResumeForEngineStart() { if (_lockdown.IsActive) _stopped = false; }

    // Panic and explicit stop paths restore immediately; normal Lockdown end reassembles through the director.
    internal void Stop()
    {
        _stopped = true;
        ++_epoch;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        lock (_leaveGate)
        {
            _active = null; _leaving = null;
            _busy = false; _hasLeft = false; _returning = false;
        }
        var tube = _ownedTube;
        _ownedTube = null;
        void Restore()
        {
            // The director books the fall before awaiting a knock. Cancel that booking too.
            if (tube != null) App.Possession?.UndoAll();
            tube?.HidePossessionNote();
            tube?.PanicSilence();
            tube?.StopPossessionMove();
        }
        if (Dispatcher.UIThread.CheckAccess()) Restore(); else Dispatcher.UIThread.Post(Restore);
    }

    public void Dispose()
    {
        _lockdown.LockdownActivated -= Reset;
        Stop();
    }
}
