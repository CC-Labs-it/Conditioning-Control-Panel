using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.HeroFx.cs START subset. One shared frame source, no compositor loops or Effects.
    private FrameClock? _startHeroClock;
    private readonly Stopwatch _startHeroTime = Stopwatch.StartNew();
    private double _startIgnitionAt = double.NegativeInfinity;
    private LinearGradientBrush? _startCharge;
    private bool _startHeroClosed;
    internal double StartHeroEpoch { get; private set; }

    private void InitializeStartHero()
    {
        _startHeroClock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
        _startHeroClock.Tick += (_, _) => PaintStartHero(_startHeroTime.Elapsed.TotalSeconds);
        Activated += (_, _) => ApplyStartHeroState();
        Deactivated += (_, _) => ApplyStartHeroState();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) ApplyStartHeroState();
        };
        if (Named<Button>("BtnStart") is { } button)
            button.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsEnabledProperty) ApplyStartHeroState();
            };
        AmbientFxCanvas.Env.MotionGateChanged += ApplyStartHeroState;
        void ModChanged(object? sender, Models.ModPackage mod) => Dispatcher.UIThread.Post(ApplyStartHeroState);
        CoreMods.ModChanged += ModChanged;
        Closed += (_, _) =>
        {
            _startHeroClosed = true;
            _startHeroClock.Stop();
            AmbientFxCanvas.Env.MotionGateChanged -= ApplyStartHeroState;
            CoreMods.ModChanged -= ModChanged;
        };
        ApplyStartHeroState();
    }

    internal void FlashStartIgnition()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions || _startHeroClosed) return;
        _startIgnitionAt = _startHeroTime.Elapsed.TotalSeconds;
        PaintStartHero(_startIgnitionAt);
        _startHeroClock?.Start();
    }

    internal void ApplyStartHeroState()
    {
        StartHeroEpoch = _startHeroTime.Elapsed.TotalSeconds;
        PaintStartHero(StartHeroEpoch);
    }

    // Explicit time allows headless assertions on the actual controls, including stagger and cleanup.
    internal void PaintStartHero(double seconds)
    {
        // Decoration must never interrupt Start/Stop, including a mod repaint or a late frame.
        try { PaintStartHeroCore(seconds); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Start hero paint failed"); }
    }

    private void PaintStartHeroCore(double seconds)
    {
        if (_startHeroClosed || Named<Button>("BtnStart") is not { } button) return;
        bool idle = !CoreEngine.IsRunning && !RemoteControllerConnected && button.IsEnabled;
        bool ambient = Pr4aAmbientAllowed && AmbientFxCanvas.Env.AllowAmbientLoops;
        double age = seconds - _startIgnitionAt;
        double phase = Math.Max(0, seconds - StartHeroEpoch);
        bool ignition = AmbientFxCanvas.Env.AllowTransitions && age >= 0 && age < .83;
        double width = button.Bounds.Width > 1 ? button.Bounds.Width : 320;
        var charge = _startCharge ??= new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, .35, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(), new(), new() }
        };
        if (idle)
        {
            var hsv = AmbientFxCanvas.Env.GlowColor.ToHsv();
            double saturation = Math.Max(hsv.S, .45);
            charge.GradientStops[0].Color = new HsvColor(1, hsv.H, saturation, hsv.V).ToRgb();
            charge.GradientStops[1].Color = new HsvColor(1, (hsv.H + 333) % 360, Math.Min(1, saturation + .02), hsv.V * .91).ToRgb();
            charge.GradientStops[2].Color = new HsvColor(1, (hsv.H + 38) % 360, saturation, hsv.V).ToRgb();
            double drift = ambient ? (1 - Math.Cos(phase * Math.PI / 4.5)) / 2 : .5;
            charge.GradientStops[0].Offset = ambient ? -.1 + .2 * drift : 0;
            charge.GradientStops[1].Offset = .34 + .32 * drift;
            charge.GradientStops[2].Offset = ambient ? .9 + .2 * drift : 1;
            button.Background = charge;
        }
        else if (ReferenceEquals(button.Background, charge))
        {
            // Release only our own paint; red STOP and green REMOTE always win.
            button.Bind(BackgroundProperty, button.GetResourceObservable("AccentGradientBrush"));
        }
        double travel = phase % 4.2;
        double exhale = idle && ambient && travel <= 1.35 ? travel / 1.35 : 0;
        SetStartRing("StartRingExhale", width, 5, 1 - Math.Pow(1 - exhale, 2),
            exhale <= .35 ? .7 * exhale / .35 : .7 * (1 - exhale) / .65);
        SetBurst("StartRingBurstA", 8, .55, 0);
        SetBurst("StartRingBurstB", 20, .75, .08);
        if (Named<Border>("StartIgnitionFlash") is { } flash)
            flash.Opacity = ignition && age < .3 ? .55 * Math.Pow(1 - age / .3, 2) : 0;
        double scaleX = 1, scaleY = 1;
        if (ignition && age < .1)
        {
            double dip = age < .04 ? 1 - Math.Pow(1 - age / .04, 2) : Math.Pow(1 - (age - .04) / .06, 2);
            scaleX = scaleY = 1 - .03 * dip;
        }
        else if (CoreEngine.IsRunning && button.IsEnabled && !RemoteControllerConnected && ambient)
        {
            double pulse = (1 - Math.Cos(phase * Math.PI / 2.4)) / 2;
            scaleX += .6 / width * pulse;
            scaleY += .6 / 50 * pulse;
        }
        if (button.RenderTransform is ScaleTransform scale) { scale.ScaleX = scaleX; scale.ScaleY = scaleY; }
        if ((ambient && (idle || (CoreEngine.IsRunning && button.IsEnabled && !RemoteControllerConnected))) || ignition)
            _startHeroClock?.Start();
        else _startHeroClock?.Stop();

        void SetBurst(string name, double grow, double duration, double delay)
        {
            double t = age - delay;
            bool visible = ignition && t >= 0 && t < duration;
            double progress = visible ? 1 - Math.Pow(1 - t / duration, 2) : 0;
            SetStartRing(name, width, grow, progress, visible ? .95 * (1 - progress) : 0);
        }
    }

    private void SetStartRing(string name, double width, double grow, double progress, double opacity)
    {
        if (Named<Border>(name) is not { } ring) return;
        ring.Opacity = Math.Clamp(opacity, 0, 1);
        if (ring.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 1 + grow / width * progress;
            scale.ScaleY = 1 + grow / 50 * progress;
        }
    }
}
