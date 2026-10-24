using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

internal static partial class ChaosRunHost
{
    private static double _vibeRemaining, _snapRemaining;
    private static bool _afterglow, _vibeHover;
    private static DispatcherTimer? _sweepTimer;
    // Same desktop pointer seam as the existing cursor/trail surfaces; null off X11.
    internal static Func<(PixelPoint At, bool Pressed, bool Escape)?> ReadToyPointer = X11Pointer.Read;

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
                s.PushEvent("🔸 it buzzes. hold and sweep");
                _sweepTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16),
                    DispatcherPriority.Render, (_, _) => SweepToyPointer());
                _sweepTimer.Start();
                return true;
            case "snap_field":
                foreach (var b in BubbleOverlay.Field.Bubbles.ToArray())
                {
                    if (b.Spec == null || b.Spec.IsDarter || b.Spec.IsFreeze || b.Popping) continue;
                    if (b.IsLiveThreat)
                    {
                        if (b.Snap()) BubbleOverlay.Field.ChaosDefused?.Invoke(b);
                    }
                    else if (maxed) BubbleOverlay.PopChaos(b);
                }
                _snapRemaining = 1;
                ChaosOverlayWindow.ChaosSfx.Play("freeze_trigger", 0.5f);
                if (s.Config.ColorFlashesEnabled) _fx?.Pulse(Color.FromRgb(150, 220, 255), 0.35);
                s.PushEvent(maxed ? "✋ snapped. all of it." : "✋ snapped — every live one let go");
                return true;
            default:
                // ponytail: freeze, DVD contact, rabbit motion/aim and charged arcs need their
                // BubbleService mechanics ported; do not award first_play for an inert toy.
                return false;
        }
    }

    internal static void SweepToyPointer()
    {
        if (_vibeRemaining <= 0 || !_spawning || _manualPaused || _overlay is not { IsVisible: true }) return;
        if (ReadToyPointer() is not { } pointer || (!_vibeHover && !pointer.Pressed)) return;
        // Bubble coordinates use each target screen's scale, just like hit-testing the field.
        foreach (var b in BubbleOverlay.Field.Bubbles.ToArray())
        {
            if (b.Spec == null || b.Spec.IsFreeze || b.Popping) continue;
            var screen = _overlay.Screens.All.ElementAtOrDefault(b.Screen);
            double scale = screen?.Scaling ?? 1;
            var spec = b.Spec;
            bool plain = !spec.IsDarter && !spec.IsGolden && !spec.IsHeart && !spec.IsDroplet
                && !spec.IsPrism && !spec.IsEscort && !spec.IsTease && !spec.IsBrittle;
            double hitMult = Math.Clamp(_state!.Config.HitboxScale, 1, 2);
            if (_state.MagnetEnabled && spec.IsLive) hitMult = Math.Clamp(hitMult * 1.4, 1, 2);
            double radius = plain ? Math.Max(b.Size, Math.Round(b.Size * hitMult)) / 2 : b.Size / 2;
            double dx = pointer.At.X / scale - b.CenterX, dy = pointer.At.Y / scale - b.CenterY;
            if (dx * dx + dy * dy > radius * radius) continue;
            if (b.IsLiveThreat)
            {
                if (b.Snap()) BubbleOverlay.Field.ChaosDefused?.Invoke(b);
            }
            else BubbleOverlay.PopChaos(b);
        }
    }

    private static void TickToyEffects()
    {
        if (_state == null) return;
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
                    _state.PushEvent("🔸 afterglow — it lingers");
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
        foreach (var toy in _state.ActiveToys)
            toy.IsEffectActive = toy.Id switch
            {
                "vibe_popping" => _vibeRemaining > 0,
                "snap_field" => _snapRemaining > 0,
                _ => false,
            };
    }

    private static void StopToyEffects()
    {
        _sweepTimer?.Stop();
        _vibeRemaining = _snapRemaining = 0;
        _afterglow = _vibeHover = false;
        ChaosVibeTrailOverlay.CloseActive();
        ChaosEffectBannerOverlay.End("vibe");
    }
}
