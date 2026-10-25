using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.HeroFx.cs: charge (9s), exhale (4.2s), ignition (100/300/550/830ms),
    // heartbeat (4.8s). Plain writes on the existing shared beat: no infinite Animation/Effect.
    private BeatLoop? _startHeroLoop, _startIgnitionLoop;
    private LinearGradientBrush? _startCharge;
    private IBrush? _startChargePrevious;
    private bool _startDip;
    private static readonly (double At, double V)[] StartExhaleAlpha =
        { (0, 0), (.4725, .7), (1.35, 0), (4.2, 0) };

    private void InitializeStartHero()
    {
        Activated += StartHeroWindowChanged;
        Deactivated += StartHeroWindowChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) ApplyStartHeroState();
        };
        if (Named<Button>("BtnStart") is { } button)
            button.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) ApplyStartHeroState(); };
        AmbientFxCanvas.Env.MotionGateChanged += ApplyStartHeroState;
        CoreMods.ModChanged += StartHeroModChanged;
        Closed += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged -= ApplyStartHeroState;
            CoreMods.ModChanged -= StartHeroModChanged;
            _startHeroLoop?.Stop();
            _startIgnitionLoop?.Stop();
        };
        ApplyStartHeroState();
    }

    private void StartHeroWindowChanged(object? sender, EventArgs e) => ApplyStartHeroState();
    private void StartHeroModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(ApplyStartHeroState);

    internal void ApplyStartHeroState()
    {
        try { ApplyStartHeroStateCore(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Start hero state"); }
    }

    private void ApplyStartHeroStateCore()
    {
        if (Named<Button>("BtnStart") is not { } button) return;
        _startHeroLoop?.Stop();
        bool idle = !CoreEngine.IsRunning && !RemoteControllerConnected && button.IsEnabled;
        if (idle)
        {
            if (_startCharge == null)
                _startCharge = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, .35, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new(), new() { Offset = .5 }, new() { Offset = 1 } },
                };
            TintStartHero();
            if (!ReferenceEquals(button.Background, _startCharge)) _startChargePrevious = button.Background;
            button.Background = _startCharge;
        }
        else if (ReferenceEquals(button.Background, _startCharge)) button.Background = _startChargePrevious;

        ParkStartRing("StartRingExhale");
        if (!_startDip) ScaleStartHero(button, 1, 1);
        if (_startCharge != null)
            for (int i = 0; i < 3; i++) _startCharge.GradientStops[i].Offset = i * .5;
        if (!AmbientFxCanvas.Env.AllowTransitions) EndStartIgnition();
        if (!IsVisible || !Pr4aAmbientAllowed || !AmbientFxCanvas.Env.AllowAmbientLoops || RemoteControllerConnected) return;
        if (!idle && !CoreEngine.IsRunning) return;
        _startHeroLoop ??= new BeatLoop(button, StepStartHero);
        _startHeroLoop.Start();
    }

    private void TintStartHero()
    {
        if (_startCharge == null) return;
        var c = Pr4aGlowColor;
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d <= .0001 ? 0 : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        double s = Math.Max(max <= .0001 ? 0 : d / max, .45);
        _startCharge.GradientStops[0].Color = StartHeroHsv(h, s, max);
        _startCharge.GradientStops[1].Color = StartHeroHsv(h - 27, Math.Min(1, s + .02), max * .91);
        _startCharge.GradientStops[2].Color = StartHeroHsv(h + 38, s, max);
    }

    private static Color StartHeroHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x)
            : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private double StartHeroWidth => Named<Button>("BtnStart") is { Bounds.Width: > 0 } b ? b.Bounds.Width : 320;
    private static void ScaleStartHero(Control? control, double x, double y)
    {
        if (control?.RenderTransform is ScaleTransform scale) { scale.ScaleX = x; scale.ScaleY = y; }
    }
    private void ParkStartRing(string name)
    {
        if (Named<Border>(name) is not { } ring) return;
        ring.Opacity = 0;
        ScaleStartHero(ring, 1, 1);
    }
    private void StepStartHero(double t)
    {
        if (CoreEngine.IsRunning)
        {
            if (!_startDip) ScaleStartHero(Named<Button>("BtnStart"), 1 + .6 / StartHeroWidth * BeatLoop.Breath(t, 2.4),
                1 + .6 / 50 * BeatLoop.Breath(t, 2.4));
            return;
        }
        if (_startCharge == null) return;
        double drift = BeatLoop.Breath(t, 4.5);
        _startCharge.GradientStops[0].Offset = -.1 + .2 * drift;
        _startCharge.GradientStops[1].Offset = .34 + .32 * drift;
        _startCharge.GradientStops[2].Offset = .9 + .2 * drift;
        var ring = Named<Border>("StartRingExhale");
        if (ring == null) return;
        double phase = t % 4.2, p = Math.Clamp(phase / 1.35, 0, 1), eased = 1 - (1 - p) * (1 - p);
        ring.Opacity = BeatLoop.Keys(phase, StartExhaleAlpha);
        ScaleStartHero(ring, phase > 1.4 ? 1 : 1 + 5 / StartHeroWidth * eased, phase > 1.4 ? 1 : 1 + 5d / 50 * eased);
    }

    internal void FlashStartIgnition()
    {
        // Decoration must never block the click's remote/lockdown/session decisions.
        try { FlashStartIgnitionCore(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Start ignition"); }
    }

    private void FlashStartIgnitionCore()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) return;
        _startIgnitionLoop?.Stop();
        _startDip = true;
        // ponytail: the same capped shared beat also drives these finite tracks, avoiding a second
        // animation owner on the button transform. It unsubscribes after the staggered ring ends.
        _startIgnitionLoop = new BeatLoop(Named<Button>("BtnStart")!, StepStartIgnition);
        _startIgnitionLoop.Start();
    }
    private void StepStartIgnition(double t)
    {
        if (_startDip)
        {
            double scale = t < .04 ? 1 - .03 * Ease(t / .04) : .97 + .03 * Ease((t - .04) / .06);
            ScaleStartHero(Named<Button>("BtnStart"), scale, scale);
            if (t >= .1) { _startDip = false; ApplyStartHeroState(); }
        }
        if (Named<Border>("StartIgnitionFlash") is { } wash) wash.Opacity = .55 * (1 - Ease(t / .3));
        Burst("StartRingBurstA", t, 8, .55);
        Burst("StartRingBurstB", t - .08, 20, .75);
        if (t >= .83) EndStartIgnition();
    }
    private static double Ease(double p) { p = Math.Clamp(p, 0, 1); return 1 - (1 - p) * (1 - p); }
    private void Burst(string name, double t, double grow, double duration)
    {
        if (Named<Border>(name) is not { } ring) return;
        double p = Ease(t / duration);
        ring.Opacity = t < 0 ? 0 : .95 * (1 - p);
        ScaleStartHero(ring, 1 + grow / StartHeroWidth * p, 1 + grow / 50 * p);
    }
    private void EndStartIgnition()
    {
        _startIgnitionLoop?.Stop();
        _startDip = false;
        ParkStartRing("StartRingBurstA"); ParkStartRing("StartRingBurstB");
        if (Named<Border>("StartIgnitionFlash") is { } wash) wash.Opacity = 0;
        ScaleStartHero(Named<Button>("BtnStart"), 1, 1);
    }
}
