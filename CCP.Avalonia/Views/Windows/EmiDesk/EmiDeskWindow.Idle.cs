using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private static readonly Random IdleRng = new();
    private DispatcherTimer? _idleTimer, _swayTimer;
    private int _swayAt;
    internal Func<int, int> IdleRoll = IdleRng.Next;

    // WPF EmiDeskWindow.xaml.cs:1185-1274 and EmiDeskWindow.Alive.cs:696-732.
    public void RestartIdleBeats()
    {
        StopIdleBeats();
        if (PresentationActive || _closingForGood || !IsVisible) return;
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(IdleRng)) };
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Start();
        _swayAt = 0;
        _swayTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(SwayHold("idle")) };
        _swayTimer.Tick += OnSwayTick;
        _swayTimer.Start();
    }

    public void StopIdleBeats()
    {
        if (_idleTimer != null) { _idleTimer.Stop(); _idleTimer.Tick -= OnIdleTick; _idleTimer = null; }
        if (_swayTimer != null) { _swayTimer.Stop(); _swayTimer.Tick -= OnSwayTick; _swayTimer = null; }
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (_idleTimer == null) return;
        _idleTimer.Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(IdleRng));
        if (Busy() || _dragging || _resizing || _closingForGood || !IsVisible) return;
        bool twice = IdleRoll(EmiAlive.DoubleBlinkOneIn) == 0;
        DrawFace(EmiAlive.BlinkFace);
        After(EmiAlive.BlinkHoldMs, () =>
        {
            if (!BlinkStillOurs()) return;
            DrawFace(EmiChains.RestFace);
            if (!twice) return;
            After(EmiAlive.DoubleBlinkGapMs, () =>
            {
                if (!BlinkStillOurs()) return;
                DrawFace(EmiAlive.BlinkFace);
                After(EmiAlive.BlinkHoldMs, () =>
                {
                    if (BlinkStillOurs()) DrawFace(EmiChains.RestFace);
                });
            });
        });
    }

    private bool BlinkStillOurs() => !_closingForGood && IsVisible && !Busy() && !_dragging && !_resizing;

    private static int SwayHold(string key) => key == "idle"
        ? IdleRng.Next(EmiChains.SwayCentreMinMs, EmiChains.SwayCentreMaxMs + 1)
        : EmiChains.SwayStepMs;

    private void OnSwayTick(object? sender, EventArgs e)
    {
        if (_swayTimer == null) return;
        if (Busy() || _dragging || _resizing)
        {
            _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold("idle"));
            return;
        }
        var key = EmiChains.SwayCycle[_swayAt++ % EmiChains.SwayCycle.Count];
        SetPose(key);
        _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold(key));
    }

    internal bool IdleBeatsRunning => _idleTimer?.IsEnabled == true && _swayTimer?.IsEnabled == true;
    internal void TickIdleForTest() => OnIdleTick(null, EventArgs.Empty);
    internal void TickSwayForTest() => OnSwayTick(null, EventArgs.Empty);
    internal double SwayIntervalForTest => _swayTimer?.Interval.TotalMilliseconds ?? 0;
    internal string IdlePoseForTest => _pose;
}
