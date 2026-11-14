using System;
using System.Collections.Generic;
using Avalonia;
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
// Off-air, Shop At Home, Reruns, Pong and Browsing savers, the GIF rain offer and the wrong-channel exit; the other channels and effects remain pending.
// ONE director (WPF EmiChannels.Pick, uniform over the feasible channels): PollGlass owns the idle flip for
// every channel on this head, the GIF rain channel (EmiDeskWindow.Rain.cs) included. A new channel goes in
// GlassChannels + GlassFeasible + LandGlassChannel, never its own watcher or timer.
public partial class EmiDeskWindow
{
    private static readonly Random GlassRng = new();
    // WPF EmiChannels.All order, the channels this head can paint.
    internal static readonly string[] GlassChannels = { "pong", "rain", "browsing", "shop", "reruns", "offair" };
    // Uniform over the feasible pool (WPF EmiChannels.Pick); tests swap it and restore it in finally.
    internal static Func<IReadOnlyList<string>, string> GlassPick = pool => pool[GlassRng.Next(pool.Count)];
    // Per-window test seam: always land this channel when it is feasible, else off-air.
    internal string? GlassChannelOverride { get; set; }
    private DispatcherTimer? _glassWatch;
    private BeatLoop? _glassFrames;
    private DateTime _glassActivity = DateTime.UtcNow, _glassPhase;
    private TimeSpan _glassIdle = NextGlassIdle();
    private Canvas? _glassLayer;
    private string? _glassChannel;
    private ShopPainter? _glassShop;
    private BrowsingPainter? _glassBrowsing;
    private RerunsPainter? _glassReruns;
    private PongPainter? _glassPong;
    private Rectangle? _glassDim;
    private TextBlock? _glassSignal;
    private bool _glassLive, _glassLanded, _glassMotion;
    private double _glassScale, _nextGlassGlitchMs;

    private static TimeSpan NextGlassIdle() => EmiDebug.IdleMs is int ms
        ? TimeSpan.FromMilliseconds(ms)
        : TimeSpan.FromSeconds(30 + GlassRng.NextDouble() * 30); // WPF EmiChannels.cs:74-82

    // The partial hooks live in EmiDeskWindow.Rain.cs (one implementation each) and call these.
    private void HookGlassActivity()
    {
        PointerActivity += (_, _) => NoteGlassActivity();
        Moved += (_, _) => NoteGlassActivity();
    }

    private bool GlassClicked()
    {
        if (!_glassLive) return false;
        if (WrongInGrace) return true; // the release of the press it rode in on
        CloseGlass(); // WPF EmiChannels.cs:50-56: a saver fires nothing.
        MaybeLandWrong();
        return true;
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
        if (!_glassLive || WrongInGrace) return;
        CloseGlass();
        MaybeLandWrong();
    }

    internal bool GlassIsLive => _glassLive;
    internal bool GlassHasLanded => _glassLanded;
    internal string? GlassChannel => _glassChannel;

    internal void PollGlass(DateTime now)
    {
        if (_glassLive || _rainGlass || RainAskLive || _closingForGood || !IsVisible || PresentationActive || InputLocked ||
            Transiting || ChainLive || RingOpen || !CoreSettings.Current.EmiDeskGlass ||
            EmiLineEngine.BedtimeSet || EmiLineEngine.Instance.HoldActive || CoreEngine.Video?.IsPlaying == true)
            return;
        if (now - _glassActivity < _glassIdle) return;
        var pool = new List<string>(GlassChannels.Length);
        foreach (var id in GlassChannels) if (GlassFeasible(id)) pool.Add(id);
        var pick = ChooseGlassChannel(pool);
        if (pick == "rain")
        {
            if (TryShowRainChannel())
            {
                _glassActivity = now;
                _glassIdle = NextGlassIdle();
                return;
            }
            // No usable picture: draw again without rain, so the pick stays uniform over what can show.
            pool.Remove("rain");
            pick = ChooseGlassChannel(pool);
        }

        if (!OpenGlassLayer(pick, now)) return;
        _glassLanded = false;
        PaintGlassGlitch();
        _nextGlassGlitchMs = 55;
    }

    // One layer and ONE BeatLoop per live channel; every channel, the wrong one included, opens through here.
    private bool OpenGlassLayer(string channel, DateTime now)
    {
        var rect = GlassRect;
        if (rect.Width <= 4 || rect.Height <= 4) return false;
        _glassLayer = new Canvas { Width = rect.Width, Height = rect.Height, ClipToBounds = true, IsHitTestVisible = false };
        GlassHost.Children.Add(_glassLayer);
        _glassChannel = channel;
        _glassLive = true;
        _glassPhase = now;
        StopIdleBeats();
        _glassFrames = new BeatLoop(_glassLayer, seconds => AdvanceGlass(now.AddSeconds(seconds)));
        _glassFrames.Start();
        return true;
    }

    // WPF EmiDeskWindow.Glass.cs:480-536 and EmiChannels.cs:150-160: once a window session, 1 in 40, a channel
    // exit (tap or the user coming back) is not a clean cut and the wrong channel flashes for 1.4 s. It rides
    // exits only, never the rotation, never its own exit; full motion only, and never with Gentle visuals on
    // (it is a flash). The untouched expiry hands over to the effectDeclined hold, so it never lands there.
    internal static Func<bool> GlassRollWrong = () => GlassRng.Next(40) == 0;
    private const int WrongLifeMs = 1400;
    // A press closes the channel and lands the wrong one; that same gesture's release (or a window move
    // a few frames later) must not cut it at once. A later tap or a hold or panic always does.
    private const int WrongGraceMs = 350;
    internal bool WrongInGrace => _glassWrong != null && (DateTime.UtcNow - _glassPhase).TotalMilliseconds < WrongGraceMs;
    private bool _wrongAired;
    private WrongPainter? _glassWrong;

    private void MaybeLandWrong()
    {
        if (_wrongAired || _glassLive || _rainGlass || RainAskLive || _closingForGood || !IsVisible || PresentationActive ||
            InputLocked || Transiting || ChainLive || RingOpen || !CoreSettings.Current.EmiDeskGlass ||
            EmiLineEngine.BedtimeSet || EmiLineEngine.Instance.HoldActive || CoreEngine.Video?.IsPlaying == true ||
            AmbientFxCanvas.Env.Level != MotionLevel.Full || ConditioningControlPanel.Services.Safety.GentleVisuals.On ||
            !GlassRollWrong())
            return;
        if (!OpenGlassLayer("wrong", DateTime.UtcNow)) return;
        _wrongAired = true;
        _glassLanded = true;
        _glassWrong = new WrongPainter(_glassLayer!.Width, _glassLayer.Height, GlassRng);
        _glassWrong.Attach(_glassLayer);
        _glassWrong.Tick(0);
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
            LandGlassChannel();
            return;
        }
        if (_glassWrong != null)
        {
            // No decline and no second roll: it simply cuts out (WPF EmiDeskWindow.Glass.cs:529).
            if (elapsed >= WrongLifeMs) { CloseGlass(); return; }
            _glassWrong.Tick(elapsed);
            return;
        }
        if (elapsed >= 10_000)
        {
            // WPF EmiChannels.cs:58-59 and EmiDeskWindow.Glass.cs:472-477: an untouched channel ends in the
            // wordless effectDeclined hold (a HOLD row: a face, no bubble).
            var channel = _glassChannel ?? "offair";
            CloseGlass();
            EmiDeskService.Instance.Fire("effectDeclined", new { channel });
            return;
        }
        if (_glassPong != null)
        {
            // The moving ball IS the channel: a motion drop mid-air puts her face back (WPF EmiChannels.cs:130-135).
            if (!PongFeasible(AmbientFxCanvas.Env.Level)) { CloseGlass(); return; }
            _glassPong.Tick(elapsed);
        }
        _glassReruns?.Tick(elapsed);
        _glassShop?.Tick(elapsed);
        if (_glassMotion) _glassBrowsing?.Tick(elapsed); // the pages hold as a still below full motion
        if (_glassMotion && _glassSignal != null && _glassDim != null)
        {
            Canvas.SetLeft(_glassSignal, (10 + (int)(elapsed / 1000) % 6 * 4) * _glassScale);
            _glassDim.Opacity = .45 + .02 * Math.Sin(elapsed / 900) + (elapsed % 3300 < 60 ? -.06 : 0);
        }
    }

    // WPF EmiChannels.Feasible: a channel with nothing behind it is never offered.
    private static bool GlassFeasible(string id) => id switch
    {
        "pong" => PongFeasible(AmbientFxCanvas.Env.Level),
        "reruns" => RerunsPainter.HasMaterial(),
        // Rain refuses in TryShowRainChannel when no picture decodes; PollGlass then draws again without it.
        _ => true,
    };

    private string ChooseGlassChannel(List<string> pool)
    {
        if (pool.Count == 0) return "offair";
        var pick = GlassChannelOverride ?? GlassPick(pool);
        return pick != null && pool.Contains(pick) ? pick : "offair";
    }

    private void LandGlassChannel()
    {
        if (_glassLayer == null) return;
        if (_glassChannel == "pong" && PongFeasible(AmbientFxCanvas.Env.Level))
        {
            _glassLayer.Children.Clear();
            _glassPong = new PongPainter(_glassLayer.Width, _glassLayer.Height, GlassRng);
            _glassPong.Attach(_glassLayer);
            return;
        }
        if (_glassChannel == "reruns" && RerunsPainter.GetTape() is { } tape)
        {
            _glassLayer.Children.Clear();
            _glassReruns = new RerunsPainter(_glassLayer, tape);
            _glassReruns.Tick(0);
            return;
        }
        if (_glassChannel == "browsing")
        {
            _glassLayer.Children.Clear();
            _glassMotion = AmbientFxCanvas.Env.Level == MotionLevel.Full;
            _glassBrowsing = new BrowsingPainter(_glassLayer.Width, _glassLayer.Height);
            _glassBrowsing.Attach(_glassLayer);
            _glassBrowsing.Tick(0);
            return;
        }
        if (_glassChannel == "shop")
        {
            _glassShop = new ShopPainter(_glassLayer, GlassRng.Next(3));
            return;
        }
        _glassChannel = "offair";
        PaintOffAir();
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
        var brb = new TextBlock { Text = Loc.Get("emi_desk_offair_brb"), FontFamily = font, FontSize = size, Foreground = Brush.Parse("#F5F0E1") };
        double brbWidth = FitGlassLabel(brb, w);
        Canvas.SetLeft(brb, (w - brbWidth) / 2); Canvas.SetTop(brb, h / 2 - size); c.Children.Add(brb);
        _glassSignal = new TextBlock { Text = Loc.Get("emi_desk_offair_no_signal"), FontFamily = font, FontSize = Math.Max(5, 7 * k), Foreground = Brush.Parse("#F5F0E1"), Opacity = .85 };
        FitGlassLabel(_glassSignal, w - 30 * k); // WPF: initial 10k plus five 4k steps.
        Canvas.SetLeft(_glassSignal, 10 * k); Canvas.SetTop(_glassSignal, h - Math.Max(9, 16 * k)); c.Children.Add(_glassSignal);
        for (double y = 0; y < h; y += 2) AddGlassBox(c, 0, y, w, 1, "#000000", .16);
        _glassMotion = AmbientFxCanvas.Env.Level == MotionLevel.Full;
    }

    private static double FitGlassLabel(TextBlock label, double room)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = label.DesiredSize.Width;
        if (width <= room || width <= 0) return width;
        double scale = Math.Max(0, room) / width;
        label.RenderTransformOrigin = RelativePoint.TopLeft;
        label.RenderTransform = new ScaleTransform(scale, scale);
        return width * scale;
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
        _glassShop = null; _glassReruns = null; _glassPong = null; _glassBrowsing = null; _glassWrong = null; _glassChannel = null;
        _glassLayer = null; _glassDim = null; _glassSignal = null;
        _glassActivity = DateTime.UtcNow;
        _glassIdle = NextGlassIdle();
        if (wasLive && !_closingForGood && IsVisible && !ChainLive) RestartIdleBeats();
    }
}
