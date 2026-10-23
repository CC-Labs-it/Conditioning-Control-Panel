// WPF MainWindow.HeroFx.cs:169-558, adapted to the existing shared beat (no compositor loops).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _startAmbient, _startIgnition;
    private LinearGradientBrush? _startCharge;
    private bool _startHeroClosed, _startDipping, _startAmbientRunningState;
    internal bool StartHeroTicking => _startAmbient?.IsTicking == true;

    private void InitializeStartHero()
    {
        Activated += (_, _) => ApplyStartHeroState();
        Deactivated += (_, _) => ApplyStartHeroState();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) ApplyStartHeroState();
        };
        AmbientFxCanvas.Env.MotionGateChanged += ApplyStartHeroState;
        Closed += (_, _) =>
        {
            _startHeroClosed = true;
            AmbientFxCanvas.Env.MotionGateChanged -= ApplyStartHeroState;
            _startAmbient?.Stop(); _startIgnition?.Stop();
        };
        ApplyStartHeroState();
    }

    internal void ApplyStartHeroState()
    {
        if (_startHeroClosed || Named<Button>("BtnStart") is not { } button) return;
        bool idle = !CoreEngine.IsRunning && !RemoteControllerConnected && button.IsEnabled;
        if (idle)
        {
            var hsv = AmbientFxCanvas.Env.GlowColor.ToHsv();
            double sat = Math.Max(hsv.S, .45);
            _startCharge ??= new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, .35, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(), new(), new() }
            };
            _startCharge.GradientStops[0].Color = new HsvColor(1, hsv.H, sat, hsv.V).ToRgb();
            _startCharge.GradientStops[1].Color = new HsvColor(1, (hsv.H + 333) % 360, Math.Min(1, sat + .02), hsv.V * .91).ToRgb();
            _startCharge.GradientStops[2].Color = new HsvColor(1, (hsv.H + 38) % 360, sat, hsv.V).ToRgb();
            button.Background = _startCharge;
        }
        bool ambient = IsActive && IsVisible && WindowState != WindowState.Minimized
                       && AmbientFxCanvas.Env.AllowAmbientLoops && !RemoteControllerConnected && button.IsEnabled;
        if (!ambient)
        {
            _startAmbient?.Stop(); _startAmbient = null;
            PaintStartAmbient(0, false);
            return;
        }
        if (_startAmbientRunningState != CoreEngine.IsRunning)
        {
            _startAmbient?.Stop(); _startAmbient = null;
            _startAmbientRunningState = CoreEngine.IsRunning;
        }
        _startAmbient ??= new BeatLoop(button, t => PaintStartAmbient(t, true));
        _startAmbient.Start();
    }

    private static void StartScale(Control? control, double grow, double width, double height)
    {
        if (control?.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 1 + grow / Math.Max(1, width);
            scale.ScaleY = 1 + grow / height;
        }
    }

    internal void PaintStartAmbient(double t, bool allowed)
    {
        var button = Named<Button>("BtnStart");
        double width = button is { Bounds.Width: > 1 } ? button.Bounds.Width : 320;
        bool running = CoreEngine.IsRunning;
        if (!_startDipping) StartScale(button, allowed && running ? .6 * BeatLoop.Breath(t, 2.4) : 0, width, 50);
        var ring = Named<Border>("StartRingExhale");
        double phase = t % 4.2;
        if (ring != null)
        {
            ring.Opacity = allowed && !running ? Math.Clamp(phase < .4725 ? .7 * phase / .4725 : .7 * (1.35 - phase) / .8775, 0, .7) : 0;
            double p = Math.Clamp(phase / 1.35, 0, 1);
            StartScale(ring, allowed && !running && phase <= 1.35 ? 5 * (1 - (1 - p) * (1 - p)) : 0, width, 50);
        }
        if (_startCharge is { } charge)
        {
            double p = allowed ? BeatLoop.Breath(t, 4.5) : .5;
            charge.GradientStops[0].Offset = -.1 + .2 * p;
            charge.GradientStops[1].Offset = .34 + .32 * p;
            charge.GradientStops[2].Offset = .9 + .2 * p;
        }
    }

    internal void FlashStartIgnition()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) return;
        _startIgnition?.Stop();
        _startDipping = true;
        _startIgnition = new BeatLoop(this, PaintStartIgnition);
        _startIgnition.Start();
    }

    internal void PaintStartIgnition(double t)
    {
        double width = Named<Button>("BtnStart") is { Bounds.Width: > 1 } b ? b.Bounds.Width : 320;
        double pDip = Math.Clamp(t <= .04 ? t / .04 : (t - .04) / .06, 0, 1);
        double ease = 1 - (1 - pDip) * (1 - pDip);
        if (t <= .1) StartScale(Named<Button>("BtnStart"), (t <= .04 ? -.03 * ease : -.03 * (1 - ease)) * 50, 50, 50);
        else if (_startDipping) { _startDipping = false; _startAmbient?.Stop(); _startAmbient = null; ApplyStartHeroState(); }
        if (Named<Border>("StartIgnitionFlash") is { } wash) wash.Opacity = .55 * Math.Pow(1 - Math.Clamp(t / .3, 0, 1), 2);
        Burst("StartRingBurstA", 8, .55, 0);
        Burst("StartRingBurstB", 20, .75, .08);
        if (t >= .83) { _startIgnition?.Stop(); _startIgnition = null; }
        void Burst(string name, double grow, double duration, double delay)
        {
            if (Named<Border>(name) is not { } ring) return;
            double p = Math.Clamp((t - delay) / duration, 0, 1);
            ring.Opacity = t < delay ? 0 : .95 * (1 - p) * (1 - p);
            StartScale(ring, grow * (1 - (1 - p) * (1 - p)), width, 50);
        }
    }
}
