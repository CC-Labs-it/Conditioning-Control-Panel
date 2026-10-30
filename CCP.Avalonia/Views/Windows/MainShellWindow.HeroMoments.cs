// WPF MainWindow/MainWindow.HeroFx.cs:45-80,176-467: Start charge, exhale, ignition, heartbeat.
// WPF MainWindow/MainWindow.HeroFx.cs:104-109,702-754: Save draw, ripple, hold and fade.
// WPF MainWindow/MainWindow.xaml.cs:764-813: level-up notification and volume-curved sound.
// WPF MainWindow/MainWindow.EventFx.cs:249-262: right-edge 110-particle burst before repaint.
// Adapted to the existing shared beat and Core seams.
// ponytail: Bank counter hold stays with the unported Bank pot/flight host; never freeze XP alone.
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _startHero, _startIgnition, _saveAbsorb;
    private LinearGradientBrush? _startCharge;
    private bool _chargeApplied, _dipInFlight;
    private Action? _stopLevelSound;
    private readonly EventBurstLayer _levelBurst = new();
    internal int LevelBurstCount => _levelBurst.Count;
    internal bool StartHeroRunning => _startHero?.IsRunning == true;
    internal bool StartIgniting => _startIgnition?.IsRunning == true;
    internal bool SaveAbsorbing => _saveAbsorb?.IsRunning == true;
    private bool HeroAmbient => !_heroClosed && AmbientFxCanvas.Env.AllowAmbientLoops
        && IsActive && IsVisible && WindowState != WindowState.Minimized;

    private void HookHeroMoments()
    {
        Activated += (_, _) => ApplyStartHeroState();
        Deactivated += (_, _) => ApplyStartHeroState();
        void ModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
        {
            if (_heroClosed) return;
            UpdateLevelDisplay();
            ApplyStartHeroState();
        });
        CoreMods.ModChanged += ModChanged;
        if (Named<Button>("BtnStart") is { } button)
            button.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) ApplyStartHeroState(); };
        Closed += (_, _) =>
        {
            CoreMods.ModChanged -= ModChanged;
            _startHero?.Stop();
            StopHeroOneShots();
            _stopLevelSound?.Invoke();
        };
    }

    internal void ApplyStartHeroState()
    {
        _startHero?.Stop();
        if (_heroClosed || Named<Button>("BtnStart") is not { } button) return;
        bool idle = !CoreEngine.IsRunning && !RemoteControllerConnected && button.IsEnabled;
        bool heartbeat = CoreEngine.IsRunning && !RemoteControllerConnected;
        if (idle)
        {
            _startCharge ??= new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.35, RelativeUnit.Relative), // WPF HeroFx.cs:279 lean
                GradientStops = new GradientStops { new(), new(), new() }
            };
            var (h, s, v) = HeroHsv(AmbientFxCanvas.Env.GlowColor);
            s = Math.Max(s, 0.45);
            _startCharge.GradientStops[0].Color = HeroColor(h, s, v);
            _startCharge.GradientStops[1].Color = HeroColor(h - 27, Math.Min(1, s + 0.02), v * 0.91);
            _startCharge.GradientStops[2].Color = HeroColor(h + 38, s, v);
            button.SetCurrentValue(BackgroundProperty, _startCharge);
            _chargeApplied = true;
        }
        else if (_chargeApplied)
        {
            // SetCurrentValue preserves the running/remote background binding painted by the caller.
            _chargeApplied = false;
            if (ReferenceEquals(button.Background, _startCharge))
                button.SetCurrentValue(BackgroundProperty, CoreEngine.IsRunning
                    ? new SolidColorBrush(Color.FromRgb(255, 107, 107))
                    : (IBrush?)button.FindResource("AccentGradientBrush"));
        }
        void Paint(double seconds)
        {
            if (idle && _startCharge != null)
            {
                double drift = HeroAmbient ? BeatLoop.Breath(seconds, 4.5) : 0.5;
                _startCharge.GradientStops[0].Offset = -0.10 + 0.20 * drift;
                _startCharge.GradientStops[1].Offset = 0.34 + 0.32 * drift;
                _startCharge.GradientStops[2].Offset = 0.90 + 0.20 * drift;
            }
            double t = seconds % 4.2;
            double alpha = idle && HeroAmbient ? BeatLoop.Keys(t, new[] { (0d, 0d), (0.4725, 0.70), (1.35, 0d), (4.2, 0d) }) : 0;
            double grow = idle && HeroAmbient && t < 1.4 ? 5 * Out(t / 1.35) : 0;
            PaintHeroRing("StartRingExhale", alpha, grow, StartHeroWidth);
            if (!_dipInFlight && button.RenderTransform is ScaleTransform scale)
            {
                double swell = heartbeat && HeroAmbient ? 0.6 * BeatLoop.Breath(seconds, 2.4) : 0;
                scale.ScaleX = Grow(StartHeroWidth, swell);
                scale.ScaleY = Grow(50, swell);
            }
        }
        Paint(0);
        if (HeroAmbient && (idle || heartbeat))
        {
            _startHero = new BeatLoop(button, Paint);
            _startHero.Start();
        }
    }

    private double StartHeroWidth => Named<Grid>("StartRingHost")?.Bounds.Width is > 1 and var w ? w : 320;
    private static double Out(double p) { p = Math.Clamp(p, 0, 1); return 1 - (1 - p) * (1 - p); }
    private static double Grow(double size, double px) => size <= 1 ? 1 : (size + px) / size;
    private void PaintHeroRing(string name, double alpha, double px, double width)
    {
        if (Named<Border>(name) is not { } ring) return;
        ring.Opacity = Math.Clamp(alpha, 0, 1);
        if (ring.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = Grow(width, px);
            scale.ScaleY = Grow(50, px);
        }
    }

    internal void FlashStartIgnition()
    {
        if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions || Named<Button>("BtnStart") is not { } button) return;
        _startIgnition?.Stop();
        _dipInFlight = true;
        _startIgnition = new BeatLoop(button, t =>
        {
            if (button.RenderTransform is ScaleTransform scale && _dipInFlight)
            {
                double size = t <= 0.04 ? 1 - 0.03 * Out(t / 0.04) : 0.97 + 0.03 * Out((t - 0.04) / 0.06);
                scale.ScaleX = scale.ScaleY = size;
                if (t >= 0.1) { _dipInFlight = false; ApplyStartHeroState(); }
            }
            if (Named<Border>("StartIgnitionFlash") is { } flash) flash.Opacity = 0.55 * (1 - Out(t / 0.3));
            PaintHeroRing("StartRingBurstA", 0.95 * (1 - Out(t / 0.55)), 8 * Out(t / 0.55), StartHeroWidth);
            PaintHeroRing("StartRingBurstB", t < 0.08 ? 0 : 0.95 * (1 - Out((t - 0.08) / 0.75)),
                20 * Out((t - 0.08) / 0.75), StartHeroWidth);
            if (t >= 0.83) _startIgnition?.Stop();
        });
        _startIgnition.Start();
    }

    internal void FlashSaveAbsorb()
    {
        if (_heroClosed || !AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("SaveAbsorbHost") is not { } host) return;
        _saveAbsorb?.Stop();
        _saveAbsorb = new BeatLoop(host, t =>
        {
            if (Named<ShapePath>("SaveTick") is { } tick)
            {
                tick.StrokeDashOffset = 9 * (1 - Out(t / 0.4));
                tick.Opacity = 1 - Math.Clamp((t - 1.6) / 0.26, 0, 1);
            }
            PaintHeroRing("SaveRipple", 0.75 * (1 - Out(t / 0.52)), 14 * Out(t / 0.52), host.Bounds.Width);
            if (t >= 1.86) _saveAbsorb?.Stop();
        });
        _saveAbsorb.Start();
    }

    private void StopHeroOneShots()
    {
        _startIgnition?.Stop();
        _saveAbsorb?.Stop();
        _dipInFlight = false;
        if (Named<Button>("BtnStart")?.RenderTransform is ScaleTransform scale)
            scale.ScaleX = scale.ScaleY = 1;
        foreach (var name in new[] { "StartIgnitionFlash", "SaveTick" })
            if (Named<Control>(name) is { } visual) visual.Opacity = 0;
        foreach (var name in new[] { "StartRingBurstA", "StartRingBurstB", "SaveRipple" })
            PaintHeroRing(name, 0, 0, 320);
    }

    internal void StopHeroForPanic()
    {
        StopHeroOneShots();
        _stopLevelSound?.Invoke();
        _stopLevelSound = null;
    }

    internal void CelebrateHeroLevelUp(int level)
    {
        if (_heroClosed) return;
        FlashLevelUp();
        PopLevelChip();
        if (Named<Border>("XPBarTrack") is { } track) _levelBurst.Fire(track, 110, rightEdge: true);
        UpdateLevelDisplay();
        Platform.OsNotifications.Show(Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", level));
        PlayHeroLevelSound();
    }

    private void PlayHeroLevelSound()
    {
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var path = new[] { "Resources/sounds/lvup.mp3", "Resources/lvlup.mp3", "assets/sounds/lvlup.mp3" }
                .Select(p => System.IO.Path.Combine(root, p)).FirstOrDefault(File.Exists);
            if (path == null) return;
            _stopLevelSound?.Invoke();
            float volume = Math.Max(0.01f, (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5) * 0.2625f);
            _stopLevelSound = CoreAudio.PlayStoppable(path, volume, "level-up");
        }
        catch (Exception ex) { Serilog.Log.Debug("Level up sound: {E}", ex.Message); }
    }

    private static (double H, double S, double V) HeroHsv(Color c)
    {
        double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d < 0.0001 ? 0 : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h < 0 ? h + 360 : h, max < 0.0001 ? 0 : d / max, max);
    }
    private static Color HeroColor(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x)
            : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
