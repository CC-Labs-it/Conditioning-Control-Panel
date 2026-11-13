using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

internal static partial class ChaosRunHost
{
    private static double _vibeRemaining, _snapRemaining, _thoughtAccumSec;
    // WPF ChaosModeService.cs:3009-3011: a maxed Intrusive Thoughts arms the rabbit split (#3555).
    internal static Action<double, double, double, string> LaunchThought = (sec, speed, scale, text) =>
        ChaosDvdOverlay.Launch(_overlay!, sec, speed, scale, text: text,
            splitOnRabbit: _state?.MaxedBoons.Contains("intrusive_thoughts") == true);
    private static bool _afterglow, _vibeHover;
    private static FrameClock? _sweepTimer;
    // Same desktop pointer seam as the existing cursor/trail surfaces; null off X11.
    internal static Func<(PixelPoint At, bool Pressed, bool Escape)?> ReadToyPointer = X11Pointer.Read;

    // WPF ChaosModeService.cs:2697-2709,2736-2749,2779-2780.
    private static bool FireToyEffect(ChaosToyState toy)
    {
        var s = _state!;
        double power = s.ToyPower.TryGetValue(toy.Id, out var p) ? p : 0;
        bool maxed = s.MaxedBoons.Contains(toy.Id);
        switch (toy.Id)
        {
            case "vibe_popping":
                _vibeRemaining = Math.Max(1, power);
                _afterglow = false;
                _vibeHover = maxed;
                ChaosVibeTrailOverlay.Start();
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Color.FromRgb(0xFF, 0xB0, 0x3A));
                ChaosOverlayWindow.ChaosSfx.Play("vibe_buzz", 0.5f);
                s.PushEvent(Loc.Get("chaos_toy_buzz"));
                if (_sweepTimer == null)
                {
                    _sweepTimer = new FrameClock(_overlay!) { Interval = TimeSpan.FromMilliseconds(16) };
                    _sweepTimer.Tick += (_, _) => SweepToyPointer();
                }
                _sweepTimer.Start();
                return true;
            case "snap_field":
                foreach (var b in BubbleOverlay.Field.Bubbles.ToArray())
                {
                    if (b.Spec == null || b.Spec.IsDarter || b.Spec.IsFreeze || b.Popping) continue;
                    if (maxed || b.IsLiveThreat) BubbleOverlay.PopChaos(b);
                }
                _snapRemaining = 1;
                ChaosOverlayWindow.ChaosSfx.Play("freeze_trigger", 0.5f);
                if (s.Config.ColorFlashesEnabled) _fx?.Pulse(Color.FromRgb(150, 220, 255), 0.35);
                s.PushEvent(Loc.Get(maxed ? "chaos_toy_snap_all" : "chaos_toy_snap_live"));
                return true;
            case "porn_dvd":
            {
                if (_overlay == null) return false;
                // WPF ChaosModeService.cs:2722-2734, :2780. Reuse the existing toy gates and flock.
                int lvl = ChaosMeta.BoonLevel(toy.Id);
                double speed = lvl switch { 1 => 0.7, 2 => 0.85, _ => 1.0 };
                double scale = lvl switch { 1 => 0.8, 2 => 0.9, _ => 1.0 };
                ChaosDvdOverlay.Launch(_overlay, Math.Max(5, power), speed, scale,
                    count: maxed ? 2 : 1, splitBounces: s.DvdSplitBounces);
                ChaosOverlayWindow.ChaosSfx.Play("dvd_launch", 0.55f);
                ChaosEffectBannerOverlay.Show("dvd", Loc.Get("chaos_dvd_title"), Color.FromRgb(0xFF, 0x69, 0xB4));
                _dvdBannerOn = true;
                // WPF ChaosModeService.cs:2733; the HUD draws the catalogue prefix as an IconGlyph.
                s.PushEvent($"{toy.Glyph} {Loc.Get("chaos_dvd_loading")}");
                return true;
            }
            case "e_stim":
            {
                // WPF ChaosModeService.cs:2761-2775 (hand-ported from #4356): arm charged pops.
                // The toy's Glyph prefixes each event line; the HUD draws it as an IconGlyph.
                int charges = Math.Max(1, (int)power) * Math.Max(1, s.EStimChargeMult);
                var violet = Color.FromRgb(0x9C, 0x5C, 0xFF);
                string glyph = toy.Glyph;
                BubbleOverlay.EStimArc = left =>
                {
                    ChaosOverlayWindow.ChaosSfx.Play("estim_zap", 0.6f);
                    Pulse(violet, 0.25);
                    if (left == 0) ChaosEffectBannerOverlay.End("estim");
                    _state?.PushEvent($"{glyph} {(left > 0 ? Loc.GetF("Chaos_EStim_Arc", left) : Loc.Get("Chaos_EStim_Spent"))}");
                };
                BubbleOverlay.ArmEStim(charges, maxed);
                if (BubbleOverlay.EStimCharges <= 0) return false;
                ChaosOverlayWindow.ChaosSfx.Play("toy_ready", 0.5f);
                ChaosEffectBannerOverlay.Show("estim", "E-Stim", violet);
                Pulse(violet, 0.25);
                s.PushEvent($"{glyph} {Loc.GetF(maxed ? "Chaos_EStim_Chain" : "Chaos_EStim_Conduct", charges)}");
                return true;
            }
            default:
                // ponytail: freeze and rabbit motion/aim need their BubbleService
                // mechanics ported; do not award first_play for an inert toy.
                return false;
        }
    }

    // WPF BubbleService.cs:264-270,4329-4343 (scaled cursor and trained reach).
    internal static void SweepToyPointer()
    {
        if (_vibeRemaining <= 0 || !_spawning || _manualPaused || _lessonCardPaused || _overlay is not { IsVisible: true }) return;
        if (ReadToyPointer() is not { } pointer || (!_vibeHover && !pointer.Pressed)) return;
        // Bubble coordinates use each target screen's scale, just like hit-testing the field.
        foreach (var b in BubbleOverlay.Field.Bubbles.ToArray())
        {
            if (b.Spec == null || b.Spec.IsFreeze || b.Popping) continue;
            var screen = _overlay.Screens.All.ElementAtOrDefault(b.Screen);
            double scale = screen?.Scaling ?? 1;
            // Same disc rule as spawn (AmbientBubble.ChaosHitSize), read with the live run state.
            double radius = AmbientBubble.ChaosHitSize(b.Spec, b.Size, _state!.Config.HitboxScale, _state.MagnetEnabled) / 2;
            double dx = pointer.At.X / scale - b.CenterX, dy = pointer.At.Y / scale - b.CenterY;
            if (dx * dx + dy * dy > radius * radius) continue;
            BubbleOverlay.PopChaos(b);
        }
    }

    // WPF ChaosModeService.cs:2923-2948,3018-3021.
    private static void TickToyEffects()
    {
        if (_state == null) return;
        // WPF ChaosModeService.cs:2994-3005. Freeze is not yet exposed by this head.
        if (_state.IntrusiveThoughtsSec > 0)
        {
            _thoughtAccumSec += ChaosRunEngine.TICK_SEC;
            if (_thoughtAccumSec >= 5)
            {
                _thoughtAccumSec = 0;
                var enabled = CoreSettings.Current.BouncingTextPool.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
                var text = enabled is { Length: > 0 } ? enabled[Random.Shared.Next(enabled.Length)] : Loc.Get("chaos_thought_fallback");
                LaunchThought(_state.IntrusiveThoughtsSec, 1.3, 0.8, text);
            }
        }
        if (_vibeRemaining > 0)
        {
            _vibeRemaining -= ChaosRunEngine.TICK_SEC;
            if (_vibeRemaining <= 0)
            {
                if (_state.AfterglowSec > 0 && !_afterglow)
                {
                    _afterglow = true;
                    _vibeHover = true;
                    _vibeRemaining = _state.AfterglowSec;
                    _state.PushEvent(Loc.Get("chaos_toy_afterglow"));
                }
                else
                {
                    _sweepTimer?.Stop();
                    ChaosVibeTrailOverlay.Stop();
                    ChaosEffectBannerOverlay.End("vibe");
                }
            }
        }
        _snapRemaining = Math.Max(0, _snapRemaining - ChaosRunEngine.TICK_SEC);
        // WPF TickActiveToys (:3020-3027): DVD glow and banner follow toy logos, not thoughts.
        bool dvdLive = ChaosDvdOverlay.AnyToyActive;
        foreach (var toy in _state.ActiveToys)
            toy.IsEffectActive = toy.Id switch
            {
                "vibe_popping" => _vibeRemaining > 0,
                "snap_field" => _snapRemaining > 0,
                "porn_dvd" => dvdLive,
                "e_stim" => BubbleOverlay.EStimCharges > 0,
                _ => false,
            };
        if (_dvdBannerOn && !dvdLive)
        {
            ChaosEffectBannerOverlay.End("dvd");
            _dvdBannerOn = false;
        }
    }

    // WPF ChaosModeService.cs:3177-3181,3230-3234,3313-3317.
    private static void StopToyEffects()
    {
        _sweepTimer?.Stop();
        _sweepTimer = null;
        _vibeRemaining = _snapRemaining = _thoughtAccumSec = 0;
        _afterglow = _vibeHover = false;
        _dvdBannerOn = false;
        ChaosVibeTrailOverlay.CloseActive();
        ChaosEffectBannerOverlay.End("vibe");
        ChaosEffectBannerOverlay.End("estim");
    }
}
