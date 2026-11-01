using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiDeskWindow.Glass.cs:77-88, 174-215, 256-283, 294-342, 385-419, 440-477.
// This part ports the off-air saver; the other channels and their effects are still pending.
public partial class EmiDeskWindow
{
    private static readonly Random GlassRng = new();
    private DispatcherTimer? _glassWatch;
    private BeatLoop? _glassFrames;
    private DateTime _glassActivity = DateTime.UtcNow, _glassPhase;
    private TimeSpan _glassIdle = NextGlassIdle();
    private Canvas? _glassLayer;
    private Rectangle? _glassDim;
    private TextBlock? _glassSignal;
    private bool _glassLive, _glassLanded, _glassMotion;
    private double _glassScale, _nextGlassGlitchMs;

    private static TimeSpan NextGlassIdle() => EmiDebug.IdleMs is int ms
        ? TimeSpan.FromMilliseconds(ms)
        : TimeSpan.FromSeconds(30 + GlassRng.NextDouble() * 30); // WPF EmiChannels.cs:74-82

    partial void OnReadyCore()
    {
        PointerActivity += (_, _) => NoteGlassActivity();
        Moved += (_, _) => NoteGlassActivity();
    }

    partial void OnGlassLiveQuery(ref bool live) { if (_glassLive) live = true; }
    partial void OnGlassClickedCore(ref bool handled)
    {
        if (!_glassLive) return;
        handled = true;
        CloseGlass(); // WPF EmiChannels.cs:50-56: a saver fires nothing.
    }

    private void StartGlassWatch()
    {
        if (_glassWatch != null) return;
        _glassActivity = DateTime.UtcNow;
        _glassWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _glassWatch.Tick += (_, _) => PollGlass(DateTime.UtcNow);
        _glassWatch.Start();
    }

    private void StopGlassWatch()
    {
        _glassWatch?.Stop();
        _glassWatch = null;
        CloseGlass();
    }

    internal void NoteGlassActivity()
    {
        _glassActivity = DateTime.UtcNow;
        if (_glassLive) CloseGlass();
    }

    internal bool GlassIsLive => _glassLive;
    internal bool GlassHasLanded => _glassLanded;

    internal void PollGlass(DateTime now)
    {
        if (_glassLive || _closingForGood || !IsVisible || PresentationActive || InputLocked ||
            Transiting || ChainLive || RingOpen || !CoreSettings.Current.EmiDeskGlass ||
            EmiLineEngine.BedtimeSet || EmiLineEngine.Instance.HoldActive || CoreEngine.Video?.IsPlaying == true)
            return;
        if (now - _glassActivity < _glassIdle) return;

        var rect = GlassRect;
        if (rect.Width <= 4 || rect.Height <= 4) return;
        _glassLayer = new Canvas { Width = rect.Width, Height = rect.Height, ClipToBounds = true, IsHitTestVisible = false };
        GlassHost.Children.Add(_glassLayer);
        _glassLive = true;
        _glassLanded = false;
        _glassPhase = now;
        StopIdleBeats();
        PaintGlassGlitch();
        _nextGlassGlitchMs = 55;
        _glassFrames = new BeatLoop(_glassLayer, seconds => AdvanceGlass(now.AddSeconds(seconds)));
        _glassFrames.Start();
    }

    internal void AdvanceGlass(DateTime now)
    {
        if (!_glassLive) return;
        double elapsed = (now - _glassPhase).TotalMilliseconds;
        if (!_glassLanded)
        {
            if (elapsed < 220)
            {
                if (elapsed >= _nextGlassGlitchMs)
                {
                    PaintGlassGlitch();
                    _nextGlassGlitchMs = (Math.Floor(elapsed / 55) + 1) * 55;
                }
                return;
            }
            _glassLanded = true;
            _glassPhase = now;
            PaintOffAir();
            return;
        }
        if (elapsed >= 10_000) { CloseGlass(); return; } // WPF EmiChannels.cs:58-59
        if (_glassMotion && _glassSignal != null && _glassDim != null)
        {
            Canvas.SetLeft(_glassSignal, (10 + (int)(elapsed / 1000) % 6 * 4) * _glassScale);
            _glassDim.Opacity = .45 + .02 * Math.Sin(elapsed / 900) + (elapsed % 3300 < 60 ? -.06 : 0);
        }
    }

    private void PaintGlassGlitch()
    {
        if (_glassLayer == null) return;
        var c = _glassLayer;
        c.Children.Clear();
        AddGlassBox(c, 0, 0, c.Width, c.Height, "#0E0E1C", 1);
        int bands = 3 + GlassRng.Next(3);
        for (int i = 0; i < bands; i++)
        {
            double h = Math.Max(1, c.Height * (.03 + GlassRng.NextDouble() * .10));
            AddGlassBox(c, (GlassRng.NextDouble() - .5) * c.Width * .5,
                GlassRng.NextDouble() * Math.Max(1, c.Height - h), c.Width, h, "#FF69B4", .18 + GlassRng.NextDouble() * .5);
        }
    }

    // WPF EmiChannels.Screensavers.cs:292-419: off-air bars, plate, labels and hum.
    private void PaintOffAir()
    {
        if (_glassLayer == null) return;
        var c = _glassLayer;
        c.Children.Clear();
        double w = c.Width, h = c.Height, k = _glassScale = Math.Max(.1, w / 152);
        string[] bars = { "#B36A75", "#5A6B84", "#F5F0E1", "#4E8C86", "#7A5C87", "#2F2F3D" };
        for (int i = 0; i < bars.Length; i++) AddGlassBox(c, i * w / 6, 0, w / 6 + 1, h, bars[i], 1);
        _glassDim = AddGlassBox(c, 0, 0, w, h, "#000000", .45);
        var plate = new Ellipse { Width = 60 * k, Height = 60 * k, Fill = Brush.Parse("#1A1A2E") };
        Canvas.SetLeft(plate, w / 2 - 30 * k); Canvas.SetTop(plate, h / 2 - 36 * k); c.Children.Add(plate);
        var font = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
        double size = Math.Max(6, 12 * k);
        var brb = new TextBlock { Text = Loc.Get("emi_desk_offair_brb"), FontFamily = font, FontSize = size, Foreground = Brush.Parse("#F5F0E1"), Width = w, TextAlignment = TextAlignment.Center };
        Canvas.SetTop(brb, h / 2 - size); c.Children.Add(brb);
        _glassSignal = new TextBlock { Text = Loc.Get("emi_desk_offair_no_signal"), FontFamily = font, FontSize = Math.Max(5, 7 * k), Foreground = Brush.Parse("#F5F0E1"), Opacity = .85 };
        Canvas.SetLeft(_glassSignal, 10 * k); Canvas.SetTop(_glassSignal, h - Math.Max(9, 16 * k)); c.Children.Add(_glassSignal);
        for (double y = 0; y < h; y += 2) AddGlassBox(c, 0, y, w, 1, "#000000", .16);
        _glassMotion = AmbientFxCanvas.Env.Level == MotionLevel.Full;
    }

    private static Rectangle AddGlassBox(Canvas c, double x, double y, double w, double h, string color, double opacity)
    {
        var box = new Rectangle { Width = w, Height = h, Fill = Brush.Parse(color), Opacity = opacity };
        Canvas.SetLeft(box, x); Canvas.SetTop(box, y); c.Children.Add(box); return box;
    }

    private void CloseGlass()
    {
        bool wasLive = _glassLive;
        _glassLive = _glassLanded = false;
        _glassFrames?.Stop(); _glassFrames = null;
        if (_glassLayer != null) GlassHost.Children.Remove(_glassLayer);
        _glassLayer = null; _glassDim = null; _glassSignal = null;
        _glassActivity = DateTime.UtcNow;
        _glassIdle = NextGlassIdle();
        if (wasLive && !_closingForGood && IsVisible && !ChainLive) RestartIdleBeats();
    }
}
