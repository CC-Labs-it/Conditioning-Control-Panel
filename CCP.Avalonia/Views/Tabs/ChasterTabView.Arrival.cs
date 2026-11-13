using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    internal static TimeProvider ArrivalTime = TimeProvider.System;
    private readonly List<(Control Card, int Delay)> _arrivalCards = new();
    private IDisposable? _arrivalVisibility;
    private BeatLoop? _arrivalBeat;
    private long _arrivalStart;
    private bool _arrivalShown;
    private int _arrivalRiseMs = 220;

    private void InitArrival()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _arrivalVisibility = EffectiveVisibility.Watch(this, ArrivalVisibilityChanged);
            Env.MotionGateChanged += ArrivalMotionChanged;
            ArrivalVisibilityChanged();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _arrivalVisibility?.Dispose(); _arrivalVisibility = null;
            Env.MotionGateChanged -= ArrivalMotionChanged;
            _arrivalShown = false;
            FinishArrival();
        };
    }

    private void ArrivalVisibilityChanged()
    {
        bool shown = IsEffectivelyVisible;
        if (shown == _arrivalShown) return;
        _arrivalShown = shown;
        if (!shown) { FinishArrival(); return; }
        Dispatcher.UIThread.Post(() => { if (_arrivalShown && IsEffectivelyVisible) StartArrival(); }, DispatcherPriority.Loaded);
    }

    private void ArrivalMotionChanged() { if (!Env.AllowTransitions) FinishArrival(); }

    private void StartArrival()
    {
        FinishArrival();
        if (!Env.AllowTransitions) return;
        IEnumerable<Control> cards = FactRow.IsVisible
            ? new Control[] { HeroCard }.Concat(FactRow.Children.OfType<Control>())
            : new Control[] { HeroCard, StatTab, StatToday, StatRun, BtnPresetGentle, BtnPresetStrict,
                BtnPresetCirce, BtnPresetCustom, CostBoard, EarnBoard, JackpotRow, FootRow };
        int slot = 0;
        foreach (var card in cards)
        {
            if (!card.IsEffectivelyVisible) continue;
            card.Opacity = 0;
            _arrivalCards.Add((card, 40 * Math.Min(slot++, 6)));
        }
        // Gentle visuals: a fade only lengthens, with the 300 ms rise floor.
        _arrivalRiseMs = ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(220);
        _arrivalStart = ArrivalTime.GetTimestamp();
        if (_arrivalCards.Count == 0) return;
        _arrivalBeat ??= new BeatLoop(this, _ => StepArrival());
        _arrivalBeat.Start();
    }

    internal void StepArrival()
    {
        if (!IsEffectivelyVisible || !Env.AllowTransitions) { FinishArrival(); return; }
        double ms = ArrivalTime.GetElapsedTime(_arrivalStart).TotalMilliseconds;
        foreach (var (card, delay) in _arrivalCards)
        {
            double p = Math.Clamp((ms - delay) / _arrivalRiseMs, 0, 1);
            card.Opacity = 1 - (1 - p) * (1 - p);
        }
        if (ms >= 240 + _arrivalRiseMs) FinishArrival();
    }

    private void FinishArrival()
    {
        _arrivalBeat?.Stop();
        foreach (var (card, _) in _arrivalCards) card.Opacity = 1;
        _arrivalCards.Clear();
    }
}
