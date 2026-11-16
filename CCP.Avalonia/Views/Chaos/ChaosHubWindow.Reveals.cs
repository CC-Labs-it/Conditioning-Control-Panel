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
    }

    internal static Action<string, float> PlayRevealSfx = ChaosOverlayWindow.ChaosSfx.Play;

    private readonly List<RevealPulse> _revealPulses = new();
    private readonly List<double> _revealChimes = new();
    private BeatLoop? _revealLoop;
    private long _revealStarted;
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
            // WPF replaces the animations but their delayed chime timers keep running.
            // Keep one beat alive so old chimes and the new batch share its timeline.
            double elapsed = _revealLoop == null ? 0 : BeatLoop.Time.GetElapsedTime(_revealStarted).TotalSeconds;
            foreach (var pulse in _revealPulses) pulse.Target.Opacity = 1;
            _revealPulses.Clear();
            string? first = null;
            foreach (var id in RevealService.PendingIds())
            {
                if (map.TryGetValue(id, out var target) && target is { IsVisible: true })
                {
                    double begin = elapsed + _revealPulses.Count * 0.6;
                    _revealPulses.Add(new(id, target, begin));
                    _revealChimes.Add(begin);
                    first ??= id;
                }
                else RevealService.MarkSeen(id);
            }
            if (first == null) return;
            if (_revealLoop == null)
            {
                _revealStarted = BeatLoop.Time.GetTimestamp();
                _revealLoop = new BeatLoop(this, AdvanceRevealFlashes);
                _revealLoop.Start();
            }
            else AdvanceRevealFlashes(elapsed);
            Platform.BarkHead.Engine?.Raise("ChaosRevealFlash", c => c.Set("element", first));
        }
        catch (Exception ex) { Log.Warning("RunRevealFlashes failed ({E})", ex.Message); }
    }

    // Explicit elapsed time keeps the finite animation and its state boundary testable
    // without sleeping or changing the production clock. No forever composition loop.
    internal void AdvanceRevealFlashes(double elapsed)
    {
        foreach (double begin in _revealChimes.ToArray())
        {
            if (elapsed < begin) continue;
            _revealChimes.Remove(begin);
            PlayRevealSfx("reveal_chime", 0.5f);
        }
        foreach (var pulse in _revealPulses.ToArray())
        {
            double t = elapsed - pulse.Begin;
            if (t < 0) continue;
            if (t >= 3)
            {
                pulse.Target.Opacity = 1;
                RevealService.MarkSeen(pulse.Id);
                _revealPulses.Remove(pulse);
            }
            else pulse.Target.Opacity = 0.625 + 0.375 * Math.Cos(t * 2 * Math.PI);
        }
        if (_revealPulses.Count == 0 && _revealChimes.Count == 0) { _revealLoop?.Stop(); _revealLoop = null; }
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
        _revealChimes.Clear();
    }
}
