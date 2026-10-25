using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

public partial class ChaosHubWindow
{
    private sealed class RevealPulse(string id, Control target, double begin)
    {
        public readonly string Id = id;
        public readonly Control Target = target;
        public readonly double Begin = begin;
        public bool Chimed;
    }

    internal static Action<string, float> PlayRevealSfx = ChaosOverlayWindow.ChaosSfx.Play;

    private readonly List<RevealPulse> _revealPulses = new();
    private BeatLoop? _revealLoop;
    private bool _revealClosed;

    // WPF ChaosHubWindow.Reveals.cs:168-244: one batch bark, silent consumption for
    // unmapped/hidden surfaces, three 500ms sine pulses with reverse, 600ms stagger.
    internal void RunRevealFlashes()
    {
        try
        {
            RevealService.Sync("hub_open");
            var map = new Dictionary<string, Control?>
            {
                [RevealIds.TabLookingGlass] = _tabImprove,
                [RevealIds.SectionToys] = this.FindControl<Control>("HdrToys"),
                [RevealIds.SectionAccessories] = this.FindControl<Control>("HdrAccessories"),
                [RevealIds.HerCorner] = _herCornerCard,
                [RevealIds.PillTeasing] = _segMedium,
                [RevealIds.PillRelentless] = _segHard,
                [RevealIds.PillInescapable] = _segExtreme,
                [RevealIds.StartPicker] = this.FindControl<Control>("CardMantras"),
                [RevealIds.Diary] = _hdrDiary,
                [RevealIds.StatsPanel] = this.FindControl<Control>("HdrStats"),
            };
            foreach (var id in RevealService.PendingIds())
            {
                if (map.TryGetValue(id, out var target) && target is { IsVisible: true })
                    _revealPulses.Add(new(id, target, _revealPulses.Count * 0.6));
                else RevealService.MarkSeen(id);
            }
            if (_revealPulses.Count == 0) return;
            // The window's shared beat (AmbientBeatLoopsTests), not a per-view frame timer.
            // Start steps t=0 at once: the first chime, then the batch bark, as before.
            _revealLoop = new BeatLoop(this, AdvanceRevealFlashes);
            _revealLoop.Start();
            Platform.BarkHead.Engine?.Raise("ChaosRevealFlash", c => c.Set("element", _revealPulses[0].Id));
        }
        catch (Exception ex) { Log.Warning("RunRevealFlashes failed ({E})", ex.Message); }
    }

    // Explicit elapsed time keeps the finite animation and its state boundary testable
    // without sleeping or changing the production clock. No forever composition loop.
    internal void AdvanceRevealFlashes(double elapsed)
    {
        foreach (var pulse in _revealPulses.ToArray())
        {
            double t = elapsed - pulse.Begin;
            if (t < 0) continue;
            if (!pulse.Chimed)
            {
                pulse.Chimed = true;
                PlayRevealSfx("reveal_chime", 0.5f);
            }
            if (t >= 3)
            {
                pulse.Target.Opacity = 1;
                RevealService.MarkSeen(pulse.Id);
                _revealPulses.Remove(pulse);
            }
            else pulse.Target.Opacity = 0.625 + 0.375 * Math.Cos(t * 2 * Math.PI);
        }
        if (_revealPulses.Count == 0) { _revealLoop?.Stop(); _revealLoop = null; }
    }

    private void StopRevealFlashes()
    {
        _revealClosed = true;
        _revealLoop?.Stop();
        _revealLoop = null;
        // Teardown settles already-started reveals without leaving timers or delayed sounds.
        foreach (var pulse in _revealPulses)
        {
            pulse.Target.Opacity = 1;
            RevealService.MarkSeen(pulse.Id);
        }
        _revealPulses.Clear();
    }
}
