using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _prestigeBeat;
    internal bool PrestigeSweeping => _prestigeBeat?.IsRunning == true;

    // WPF MainWindow.EventFx.cs:428-455: purchased node before tree rebuild, rail fallback,
    // and rank crossing chains the prestige-row burst and one full-window sheen.
    internal void CelebrateEnhancementPurchase(Control node, long rankBefore, Control? prestige)
    {
        try
        {
            if (_eventFxClosed) return;
            if (!_eventBursts.Fire(node, 100)) FireEventBurst(NavAnchorForTab("enhancements"), 100);
            if (1 + (App.Achievements?.Progress.LifetimeSkillPointsSpent ?? 0) / 100 <= rankBefore
                || !Env.AllowParticles || !IsVisible || !IsActive || WindowState == WindowState.Minimized) return;
            if (prestige == null || !_eventBursts.Fire(prestige, 150, color: Env.GlowColor))
                FireEventBurst(NavAnchorForTab("enhancements"), 150, color: Env.GlowColor);
            var host = Named<Grid>("EventFxHost");
            var band = Named<Border>("PrestigeSheen");
            var slide = (band?.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (host == null || band == null || slide == null || host.Bounds.Width <= 0) return;
            ParkPrestigeSheen();
            _prestigeBeat = new BeatLoop(band, seconds =>
            {
                if (_eventFxClosed || !Env.AllowParticles || !IsActive || WindowState == WindowState.Minimized)
                { ParkPrestigeSheen(); return; }
                double p = Math.Clamp(seconds / 1.15, 0, 1);
                slide.X = -band.Width + (host.Bounds.Width + band.Width * 1.25) * ((1 - Math.Cos(Math.PI * p)) / 2);
                band.Opacity = BeatLoop.Keys(p, new[] { (0.0, 0.0), (0.20, 0.16), (0.75, 0.16), (1.0, 0.0) });
                if (p >= 1) ParkPrestigeSheen();
            }) { OnParked = ParkPrestigeSheen };
            _prestigeBeat.Start();
        }
        catch (Exception ex) { Serilog.Log.Debug("Enhancement celebration: {Error}", ex.Message); }
    }

    private void ParkPrestigeSheen()
    {
        _prestigeBeat?.Stop();
        _prestigeBeat = null;
        if (Named<Border>("PrestigeSheen") is { } band) band.Opacity = 0;
    }
}
