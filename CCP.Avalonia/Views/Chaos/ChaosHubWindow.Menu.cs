using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

public partial class ChaosHubWindow
{
    private readonly Bitmap?[] _menuFrames = new Bitmap?[6];
    private (int frame, int hold)[] _menuSeq = Array.Empty<(int, int)>();
    // The flipbook dwell is a plain hold timer; the crossfade and breathing ride the window's
    // shared 30 fps beat (BeatLoop, parked while the menu art is hidden).
    private DispatcherTimer? _menuTimer;
    private BeatLoop? _menuMotion;
    private ImageBrush? _menuBase, _menuTop;
    private Border? _menuTopBox;
    private int _menuPos, _menuShownFrame, _menuBaseFrame;
    private ChaosHubMenuFx? _menuFx;
    private Grid? _menuArtMotion;
    private long _menuIntroStart;
    private long _menuClickReady, _menuFadeStart, _menuMotionStart;
    private bool _menuFading, _menuArtOn;

    // WPF ChaosHubWindow.xaml.cs:2067-2078.
    internal static (int frame, int hold)[] BuildFlipSeq(Bitmap?[] frames)
    {
        var seq = new List<(int, int)>();
        void Add(int i, int hold) { if (i < frames.Length && frames[i] != null) seq.Add((i, hold)); }
        Add(0, 6000); Add(1, 1900); Add(0, 6000); Add(4, 2300);
        Add(0, 6000); Add(2, 3000); Add(0, 6000); Add(3, 2700);
        Add(0, 6000); Add(5, 2500);
        if (seq.Count == 0) seq.Add((0, 6000));
        return seq.ToArray();
    }

    private void SetupMenuMotion()
    {
        _menuFx = Part<ChaosHubMenuFx>("MenuFog");
        _menuArtMotion = Part<Grid>("MenuArtMotion");
        _menuBase = (ImageBrush)Part<Border>("MenuArtBaseBox").Background!;
        _menuTopBox = Part<Border>("MenuArtTopBox");
        _menuTop = (ImageBrush)_menuTopBox.Background!;
        _menuTimer = new DispatcherTimer();
        _menuTimer.Tick += (_, _) => AdvanceMenuFrame();
        _menuMotion = new BeatLoop(_menuArtClip, _ => StepMenuMotion());
        // Up to eight PNG decodes: off the UI thread, then applied in one post. A render
        // (--render-all, tests) decodes inline so the first frame already shows the art.
        if (RenderProof.Rendering) ApplyMenuArt(LoadMenuArt());
        else Task.Run(LoadMenuArt).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Dispatcher.UIThread.Post(() => ApplyMenuArt(t.Result));
        }, TaskScheduler.Default);
    }

    private static (Bitmap?[] Frames, Bitmap? Still, bool Banner) LoadMenuArt()
    {
        var frames = new Bitmap?[6];
        for (int i = 0; i < 6; i++) frames[i] = ChaosArt.MenuImage($"menu_{i + 1}.png");
        if (frames[0] != null && frames[1] != null && frames[2] != null) return (frames, null, false);
        var still = ChaosArt.MenuImage("menu.png");
        return still != null ? (frames, still, false) : (frames, ChaosArt.MenuImage("banner.png"), true);
    }

    private void ApplyMenuArt((Bitmap?[] Frames, Bitmap? Still, bool Banner) art)
    {
        if (_menuBase == null) return;
        if (art.Still == null && !art.Banner)
        {
            Array.Copy(art.Frames, _menuFrames, _menuFrames.Length);
            _menuSeq = BuildFlipSeq(_menuFrames);
            _menuBase.Source = _menuFrames[0];
        }
        else
        {
            _menuBaseFrame = _menuShownFrame = -1;
            _menuBase.Source = art.Still;
            if (art.Banner) _menuBase.Stretch = Stretch.Uniform;
        }
        if (_menuArtOn) StartMenuArt();   // the menu was already showing while the frames decoded
    }

    private void StartMenuIntro() { _menuIntroStart = Environment.TickCount64; _menuArtClip.Opacity = 0; }

    private void StartMenuArt()
    {
        _menuArtOn = true;
        if (_menuTimer == null || _menuMotion == null) return;
        if (_menuSeq.Length > 0 && !_menuTimer.IsEnabled)
        {
            _menuTimer.Interval = TimeSpan.FromMilliseconds(_menuSeq[_menuPos].hold);
            _menuTimer.Start();
        }
        _menuMotionStart = Environment.TickCount64;
        StepMenuMotion();
        _menuMotion.Start();
    }

    private void StopMenuArt()
    {
        _menuArtOn = false;
        _menuTimer?.Stop();
        _menuMotion?.Stop();
    }

    private void AdvanceMenuFrame()
    {
        if (_menuSeq.Length == 0 || _menuTimer == null || _menuTop == null) return;
        _menuPos = (_menuPos + 1) % _menuSeq.Length;
        var step = _menuSeq[_menuPos];
        _menuTimer.Interval = TimeSpan.FromMilliseconds(step.hold);
        if (step.frame == _menuShownFrame) return;
        int previous = _menuShownFrame;
        _menuShownFrame = step.frame;
        if (_menuFading && _menuBase != null)
        {
            _menuBase.Source = _menuTop.Source;
            _menuBaseFrame = previous;
            if (_menuTopBox != null) _menuTopBox.Opacity = 0;
        }
        _menuTop.Source = _menuFrames[step.frame];
        _menuFadeStart = Environment.TickCount64;
        _menuFading = true;
    }

    private void MenuArt_Click(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_menuArtClip).Properties.IsLeftButtonPressed) AdvanceMenuClick(Environment.TickCount64);
    }

    internal void AdvanceMenuClick(long now)
    {
        if (_menuSeq.Length == 0 || now < _menuClickReady) return;
        AdvanceMenuFrame();
        _menuClickReady = now + 1100 + _menuSeq[_menuPos].hold;
        if (_menuTimer?.IsEnabled == true) { _menuTimer.Stop(); _menuTimer.Start(); }
    }

    private void StepMenuMotion()
    {
        long now = Environment.TickCount64;
        if (_menuFading && _menuTopBox != null && _menuBase != null && _menuTop != null)
        {
            // A 550 ms crossfade (fade only, no flicker): kept under Gentle Visuals and motion off.
            double t = Math.Clamp((now - _menuFadeStart) / 550.0, 0, 1);
            _menuTopBox.Opacity = (1 - Math.Cos(Math.PI * t)) / 2;
            if (t >= 1) { _menuBase.Source = _menuTop.Source; _menuBaseFrame = _menuShownFrame; _menuTopBox.Opacity = 0; _menuFading = false; }
        }
        _menuFx?.SetFrames(_menuBaseFrame, _menuShownFrame, _menuFading ? _menuTopBox?.Opacity ?? 0 : 0);
        double intro = Math.Clamp((now - _menuIntroStart) / 1100.0, 0, 1);
        double ease = 1 - Math.Pow(1 - intro, 3);
        _menuArtClip.Opacity = ease;
        // WPF's breathing zoom and tiny rotate; motion off holds the 1.02 baseline. The WPF glow
        // pulse is not ported: it animates a DropShadowEffect, which re-blurs every frame (P82).
        bool still = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level == MotionLevel.Off;
        double seconds = still ? 0 : (now - _menuMotionStart) / 1000.0;
        double scale = 1.02 + .015 * (1 - Math.Cos(Math.PI * seconds / 6.5)) / 2;
        double angle = still ? 0 : -.2 * Math.Cos(Math.PI * seconds / 7.5);
        _menuFx?.SetMotion(scale * (1.10 - .10 * ease), angle, (1 - ease) * .03);
        if (_menuArtMotion != null)
        {
            var introTransform = new TransformGroup();
            introTransform.Children.Add(new ScaleTransform(1.10 - .10 * ease, 1.10 - .10 * ease));
            introTransform.Children.Add(new TranslateTransform(0, (1 - ease) * _menuArtClip.Bounds.Height * .03));
            _menuArtMotion.RenderTransform = introTransform;
        }
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new RotateTransform(angle));
        if (_menuBase != null && _menuTop != null)
        {
            _menuBase.TransformOrigin = global::Avalonia.RelativePoint.Center;
            _menuTop.TransformOrigin = global::Avalonia.RelativePoint.Center;
            _menuBase.Transform = transform;
            _menuTop.Transform = transform;
        }
    }
}
