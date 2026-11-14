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
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiDeskWindow.Glass.cs:77-88, 174-215, 256-283, 294-342, 385-419, 440-477.
// Off-air and its wrong-channel exit are wired; the other channels remain pending.
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
    private double _glassScale;
    private WrongPainter? _wrongPainter;
    private bool _wrongAired;
    internal Func<bool>? GlassWrongRoll { get; set; }
    internal bool GlassIsWrong => _wrongPainter != null;

    private DispatcherTimer? _holdFaceTimer;

    // WPF EmiDeskWindow.Bubble.cs:545-577: a hold closes the channel and wears a quiet face.
    internal void HoldFace(LineDraw line)
    {
        CancelHoldFace();
        CloseGlass();
        CancelChain();
        StopIdleBeats();
        EmiLineEngine.Instance.Ack(line.Id, spoke: false);
        DrawFace(string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face);
        _holdFaceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(line.HoldMs > 0 ? line.HoldMs : 1400) };
        _holdFaceTimer.Tick += (_, _) =>
        {
            CancelHoldFace();
            DrawFace(EmiChains.RestFace);
            RestartIdleBeats();
        };
        _holdFaceTimer.Start();
    }

    private void CancelHoldFace()
    {
        _holdFaceTimer?.Stop();
        _holdFaceTimer = null;
    }

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
        CloseGlass(rollWrong: true); // WPF EmiChannels.cs:50-56: a saver fires nothing.
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
        CancelHoldFace();
        CloseGlass();
    }

    internal void NoteGlassActivity()
    {
        _glassActivity = DateTime.UtcNow;
        if (_glassLive) CloseGlass(rollWrong: true);
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
        // WPF EmiDeskWindow.Glass.cs:34,321-332: four tears at 55 ms intervals.
        int lastTear = -1;
        _glassFrames = new BeatLoop(this, elapsed =>
        {
            double ms = elapsed * 1000;
            if (!_glassLanded && ms < 220)
            {
                int tear = (int)(ms / 55);
                if (tear == lastTear) return;
                lastTear = tear;
            }
            AdvanceGlass(now.AddSeconds(elapsed));
        });
        _glassFrames.Start();
    }

    internal void AdvanceGlass(DateTime now)
    {
        if (!_glassLive) return;
        double elapsed = (now - _glassPhase).TotalMilliseconds;
        if (!_glassLanded)
        {
            if (elapsed < 220) { PaintGlassGlitch(); return; } // WPF EmiChannels.cs:85-86
            _glassLanded = true;
            _glassPhase = now;
            PaintOffAir();
            return;
        }
        if (_wrongPainter != null)
        {
            if (elapsed >= 1400) { CloseGlass(); return; }
            _wrongPainter.Tick(elapsed);
            return;
        }
        if (elapsed >= 10_000) { CloseGlass(rollWrong: true, now); return; } // WPF EmiChannels.cs:58-59
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

    // WPF Glass.cs:440-489: only a live channel exit can roll, once per desk session.
    private void CloseGlass(bool rollWrong = false, DateTime? now = null)
    {
        bool wasLive = _glassLive;
        bool wasWrong = _wrongPainter != null;
        bool landWrong = rollWrong && wasLive && !wasWrong && !_wrongAired && !_closingForGood &&
            IsVisible && !ChainLive && !PresentationActive && !InputLocked && !Transiting && !RingOpen &&
            AmbientFxCanvas.Env.Level == MotionLevel.Full && (GlassWrongRoll?.Invoke() ?? GlassRng.Next(40) == 0);
        _glassLive = _glassLanded = false;
        _glassFrames?.Stop(); _glassFrames = null;
        if (_glassLayer != null) GlassHost.Children.Remove(_glassLayer);
        _glassLayer = null; _glassDim = null; _glassSignal = null; _wrongPainter = null;
        _glassActivity = DateTime.UtcNow;
        _glassIdle = NextGlassIdle();
        if (landWrong)
        {
            _wrongAired = true;
            LandWrong(now ?? DateTime.UtcNow);
        }
        else if (wasLive && !_closingForGood && IsVisible && !ChainLive) RestartIdleBeats();
    }

    // WPF Glass.cs:498-529: no offer or lead-in, and a hard 1400 ms lifetime.
    private void LandWrong(DateTime now)
    {
        var rect = GlassRect;
        if (rect.Width <= 4 || rect.Height <= 4) { RestartIdleBeats(); return; }
        try
        {
            _glassLayer = new Canvas { Width = rect.Width, Height = rect.Height, ClipToBounds = true, IsHitTestVisible = false };
            GlassHost.Children.Add(_glassLayer);
            _wrongPainter = new WrongPainter(rect.Width, rect.Height);
            _glassLive = _glassLanded = true;
            _glassPhase = now;
            StopIdleBeats();
            _wrongPainter.Attach(_glassLayer);
            _wrongPainter.Tick(0);
            _glassFrames = new BeatLoop(this, elapsed => AdvanceGlass(now.AddTicks((long)Math.Round(elapsed * TimeSpan.TicksPerSecond))));
            _glassFrames.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] wrong channel failed to land");
            CloseGlass();
        }
    }

}
