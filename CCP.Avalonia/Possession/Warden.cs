// WPF Services/Possession/Warden.cs:55-396: the companion's possession verbs.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class Warden : IPossessionWarden
{
    // The delegates also let the tests drive the same coordination with a deterministic tube.
    internal Func<bool> Available = () => Dispatcher.UIThread.CheckAccess() ? AvailableOnUi() : Dispatcher.UIThread.Invoke(AvailableOnUi);
    private static bool AvailableOnUi() => CoreSettings.Current.LockdownWardenEnabled &&
        CoreEngine.Video?.IsPlaying != true && AvatarTubeWindow.Live?.CanPerformPossessionMove == true;
    internal Func<PixelRect?> TubeRect = () => AvatarTubeWindow.Live is { } tube
        ? new PixelRect(tube.Position, PixelSize.FromSize(new Size(tube.Width, tube.Height), tube.DesktopScaling)) : null;
    internal Func<object, PixelRect?> TargetRect = visual => visual is Control c ? ScreenRect(c) : null;
    internal Func<PixelRect?> WindowRect = () => ScreenRect((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
    internal Func<PixelRect, CancellationToken, Task> GlideRect = (rect, ct) => AvatarTubeWindow.Live?.GlideToScreenRectAsync(rect, ct) ?? Task.CompletedTask;
    internal Func<PixelPoint, CancellationToken, bool, Task> GlidePoint = (point, ct, clamp) => AvatarTubeWindow.Live?.GlideToScreenPointAsync(point, ct, clamp) ?? Task.CompletedTask;
    internal Func<CancellationToken, Task> Home = ct => AvatarTubeWindow.Live?.ReturnHomeAsync(ct) ?? Task.CompletedTask;
    internal Func<PixelPoint, int?> ScreenBottom = point => (AvatarTubeWindow.Live?.Screens.ScreenFromPoint(point) ?? AvatarTubeWindow.Live?.Screens.Primary)?.Bounds.Bottom;
    internal Action<string?> Note = text => { if (text == null) AvatarTubeWindow.Live?.HidePossessionNote(); else AvatarTubeWindow.Live?.ShowPossessionNote(text); };
    internal Action<string, string?> Bark = (verb, reason) =>
    {
        var engine = Platform.BarkHead.Engine;
        if (engine != null) engine.Raise(PossessionBarkTriggers.Warden, c => c.Set("verb", verb));
        else if (verb == WardenRules.Stare) AvatarTubeWindow.Live?.GigglePriority(WardenRules.FallbackStareLine(reason), playSound: false, aiGenerated: false, mood: "possessive");
    };
    internal Func<int, CancellationToken, Task> Delay = (ms, ct) => Task.Delay(ms, ct);
    internal Func<DateTime> Now = () => DateTime.UtcNow;
    private DateTime _lastAppearance = DateTime.MinValue, _lastStare = DateTime.MinValue;
    private int _busy, _returnEpoch;
    private bool _hasLeft;
    private readonly object _leaveGate = new();
    private Task? _leaveTask;
    private CancellationTokenSource? _leaveCts;

    internal Warden(LockdownService? lockdown = null)
    {
        if (lockdown != null) lockdown.LockdownActivated += Reset;
    }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public bool TryTakeTube() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    public void ReleaseTube() => Interlocked.Exchange(ref _busy, 0);
    public bool IsAvailable
    {
        get { try { return !IsBusy && Available() && WardenRules.CanAppear(Now(), _lastAppearance); } catch { return false; } }
    }

    private void Reset()
    {
        Interlocked.Increment(ref _returnEpoch);
        lock (_leaveGate) { try { _leaveCts?.Cancel(); } catch (ObjectDisposedException) { } }
        _lastAppearance = _lastStare = DateTime.MinValue;
        _hasLeft = false;
        Note(null);
    }

    public async Task KnockAsync(PossessionTarget target, CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { await Dispatcher.UIThread.InvokeAsync(() => KnockAsync(target, ct)); return; }
        if (!TryTakeTube()) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WardenRules.VerbTimeout);
        try
        {
            if (TubeRect() == null || TargetRect(target.Visual) is not { } rect || rect.Width <= 0 || rect.Height <= 0) return;
            _lastAppearance = Now();
            await GlideRect(rect, timeout.Token);
            await Delay(WardenRules.KnockBeatMs, timeout.Token);
            if (TubeRect() is { } here)
            {
                int dx = (int)WardenRules.KnockShove(rect.X + rect.Width / 2.0, here.X + here.Width / 2.0);
                for (int i = 0; i < 2; i++)
                {
                    await GlidePoint(new PixelPoint(here.X + dx, here.Y), timeout.Token, true);
                    await GlidePoint(here.Position, timeout.Token, true);
                }
            }
            timeout.Token.ThrowIfCancellationRequested();
            Bark(WardenRules.Knock, null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession warden knock failed: {Error}", ex.Message); }
        finally { ReleaseTube(); ScheduleHome(ct); }
    }

    public async Task StareAsync(string reason, CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { await Dispatcher.UIThread.InvokeAsync(() => StareAsync(reason, ct)); return; }
        if (!WardenRules.CanStare(Now(), _lastStare) || !TryTakeTube()) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WardenRules.VerbTimeout);
        try
        {
            if (WindowRect() is not { } window || TubeRect() is not { } tube || window.Width <= 0) return;
            _lastAppearance = _lastStare = Now();
            var (x, y) = WardenRules.StareSpot(window.X, window.Y, window.Width, window.Height, tube.Width, tube.Height);
            await GlidePoint(new PixelPoint((int)x, (int)y), timeout.Token, true);
            timeout.Token.ThrowIfCancellationRequested();
            Bark(WardenRules.Stare, reason);
            await Delay(WardenRules.StareLingerMs, timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession warden stare failed: {Error}", ex.Message); }
        finally { ReleaseTube(); await ReturnAsync(CancellationToken.None); }
    }

    public Task LeaveAsync(CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(() => LeaveAsync(ct));
        lock (_leaveGate)
        {
            if (_hasLeft || _leaveTask is { IsCompleted: false }) return Task.CompletedTask;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(WardenRules.VerbTimeout);
            _leaveCts = cts;
            return _leaveTask = LeaveCoreAsync(cts, Volatile.Read(ref _returnEpoch));
        }
    }

    private async Task LeaveCoreAsync(CancellationTokenSource cts, int epoch)
    {
        try
        {
            if (!TryTakeTube()) return;
            try
            {
                if (TubeRect() is not { } tube || ScreenBottom(tube.Position) is not { } bottom) return;
                _lastAppearance = Now();
                await GlidePoint(new PixelPoint(tube.X, bottom), cts.Token, false);
                if (cts.IsCancellationRequested || Volatile.Read(ref _returnEpoch) != epoch) return;
                _hasLeft = true;
                int i = Random.Shared.Next(WardenRules.NoteKeys.Length);
                Note(WardenRules.NoteText(i, Loc.Get(WardenRules.NoteKeys[i])));
                Bark(WardenRules.Leave, null);
            }
            finally { ReleaseTube(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession warden leave failed: {Error}", ex.Message); }
        finally { cts.Dispose(); }
    }

    public async Task ReturnAsync(CancellationToken ct)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { await Dispatcher.UIThread.InvokeAsync(() => ReturnAsync(ct)); return; }
        Task? leave;
        Interlocked.Increment(ref _returnEpoch);
        lock (_leaveGate)
        {
            leave = _leaveTask;
            try { _leaveCts?.Cancel(); } catch (ObjectDisposedException) { }
        }
        if (leave != null) { try { await leave; } catch { } }
        lock (_leaveGate) { if (ReferenceEquals(_leaveTask, leave)) { _leaveTask = null; _leaveCts = null; } }
        try
        {
            Note(null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WardenRules.VerbTimeout);
            await Home(timeout.Token);
            if (_hasLeft) Bark(WardenRules.Return, null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession warden return failed: {Error}", ex.Message); }
        finally { _hasLeft = false; }
    }

    private async void ScheduleHome(CancellationToken ct)
    {
        try { await Delay(WardenRules.GoHomeDelayMs, ct); await Dispatcher.UIThread.InvokeAsync(() => ReturnAsync(CancellationToken.None)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug("Possession warden home failed: {Error}", ex.Message); }
    }

    private static PixelRect? ScreenRect(Control? control)
    {
        try
        {
            if (control == null || !control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || TopLevel.GetTopLevel(control) == null) return null;
            var tl = control.PointToScreen(new Point(0, 0));
            var br = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
            return new PixelRect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        }
        catch { return null; }
    }
}
