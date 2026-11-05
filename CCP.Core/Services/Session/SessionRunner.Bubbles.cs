// WPF SessionEngine.cs:1013-1067 (bursts), :818-832/:925-935 (delay/ramp),
// :1498-1525 (apply); MainWindow.Presets.cs:2232-2261 (burst frequency).
using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

public sealed partial class SessionRunner
{
    private readonly List<double> _bubbleBursts = new();
    private int _bubbleBurstIndex;
    private bool _bubbleBurstActive;
    private DateTime _bubbleBurstEnd;

    private void ApplyBubbles(Session session, AppSettings s)
    {
        var ss = session.Settings;
        _bubbleBursts.Clear();
        _bubbleBurstIndex = 0;
        _bubbleBurstActive = false;
        if (ss.BubblesEnabled && ss.BubblesIntermittent)
        {
            double t = _random.Next(2, 5);
            for (int i = 0; i < ss.BubblesBurstCount && t < session.DurationMinutes - 2; i++)
            {
                _bubbleBursts.Add(t);
                t += _random.Next(ss.BubblesGapMin, ss.BubblesGapMax + 1);
            }
        }
        s.BubblesEnabled = ss.BubblesEnabled && !ss.BubblesIntermittent && ss.BubblesStartMinute == 0;
        if (ss.BubblesEnabled)
        {
            s.BubblesFrequency = ss.BubblesFrequency;
            s.BubblesClickable = ss.BubblesClickable;
        }
        if (s.BubblesEnabled) { CoreBubbles.Start(); CoreBubbles.RefreshFrequency(); }
        else CoreBubbles.Stop();
    }

    private void TickBubbles(SessionSettings ss, double minutes, DateTime now)
    {
        if (!ss.BubblesEnabled) return;
        var s = CoreSettings.Current;
        if (ss.BubblesIntermittent)
        {
            // WPF uses wall time for the 1-2 minute burst, including time spent paused.
            if (_bubbleBurstActive && now >= _bubbleBurstEnd)
            {
                _bubbleBurstActive = false;
                SetBubblesActive(false, ss.BubblesPerBurst);
            }
            if (!_bubbleBurstActive && _bubbleBurstIndex < _bubbleBursts.Count && minutes >= _bubbleBursts[_bubbleBurstIndex])
            {
                _bubbleBurstActive = true;
                _bubbleBurstEnd = now.AddMinutes(_random.Next(1, 3));
                _bubbleBurstIndex++;
                SetBubblesActive(true, ss.BubblesPerBurst);
            }
        }
        else if (ss.BubblesStartMinute > 0 && minutes >= ss.BubblesStartMinute)
        {
            var frequency = ss.BubblesFrequency + (int)((minutes - ss.BubblesStartMinute) / 5);
            if (s.BubblesFrequency != frequency) { s.BubblesFrequency = frequency; CoreBubbles.RefreshFrequency(); }
            if (!s.BubblesEnabled) { s.BubblesEnabled = true; CoreBubbles.Start(); }
        }
    }

    // WPF MainWindow.SetBubblesActive: an already-running spawner must take the new interval too.
    private static void SetBubblesActive(bool active, int bubblesPerBurst)
    {
        var s = CoreSettings.Current;
        s.BubblesEnabled = active;
        if (active)
        {
            s.BubblesFrequency = bubblesPerBurst * 2;
            CoreBubbles.Start();
            CoreBubbles.RefreshFrequency();
        }
        else CoreBubbles.Stop();
    }
}
