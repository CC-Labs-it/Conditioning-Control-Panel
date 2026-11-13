using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

public partial class ChaosHubWindow
{
    private readonly Bitmap?[] _menuFrames = new Bitmap?[6];
    private (int frame, int hold)[] _menuSeq = Array.Empty<(int, int)>();
    private DispatcherTimer? _menuTimer, _menuMotionTimer;
    private ImageBrush? _menuBase, _menuTop;
    private Border? _menuTopBox;
    private int _menuPos, _menuShownFrame;
    private long _menuClickReady, _menuFadeStart, _menuMotionStart;
    private bool _menuFading;

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
        _menuBase = (ImageBrush)Part<Border>("MenuArtBaseBox").Background!;
        _menuTopBox = Part<Border>("MenuArtTopBox");
        _menuTop = (ImageBrush)_menuTopBox.Background!;
        for (int i = 0; i < 6; i++) _menuFrames[i] = ChaosArt.MenuImage($"menu_{i + 1}.png");
        if (_menuFrames[0] != null && _menuFrames[1] != null && _menuFrames[2] != null)
        {
            _menuSeq = BuildFlipSeq(_menuFrames);
            _menuBase.Source = _menuFrames[0];
        }
        else
        {
            _menuBase.Source = ChaosArt.MenuImage("menu.png");
            if (_menuBase.Source == null)
            {
                _menuBase.Source = ChaosArt.MenuImage("banner.png");
                _menuBase.Stretch = Stretch.Uniform;
            }
        }
        _menuTimer = new DispatcherTimer();
        _menuTimer.Tick += (_, _) => AdvanceMenuFrame();
        _menuMotionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _menuMotionTimer.Tick += (_, _) => StepMenuMotion();
    }

    private void StartMenuArt()
    {
        if (_menuTimer == null || _menuMotionTimer == null) return;
        if (_menuSeq.Length > 0)
        {
            _menuTimer.Interval = TimeSpan.FromMilliseconds(_menuSeq[_menuPos].hold);
            _menuTimer.Start();
        }
        _menuMotionStart = Environment.TickCount64;
        StepMenuMotion();
        _menuMotionTimer.Start();
    }

    private void StopMenuArt()
    {
        _menuTimer?.Stop();
        _menuMotionTimer?.Stop();
    }

    private void AdvanceMenuFrame()
    {
        if (_menuSeq.Length == 0 || _menuTimer == null || _menuTop == null) return;
        _menuPos = (_menuPos + 1) % _menuSeq.Length;
        var step = _menuSeq[_menuPos];
        _menuTimer.Interval = TimeSpan.FromMilliseconds(step.hold);
        if (step.frame == _menuShownFrame) return;
        _menuShownFrame = step.frame;
        if (_menuFading && _menuBase != null)
        {
            _menuBase.Source = _menuTop.Source;
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
            double t = Math.Clamp((now - _menuFadeStart) / 550.0, 0, 1);
            _menuTopBox.Opacity = (1 - Math.Cos(Math.PI * t)) / 2;
            if (t >= 1) { _menuBase.Source = _menuTop.Source; _menuTopBox.Opacity = 0; _menuFading = false; }
        }
        double seconds = (now - _menuMotionStart) / 1000.0;
        double scale = 1.02 + .015 * (1 - Math.Cos(Math.PI * seconds / 6.5)) / 2;
        double angle = -.2 * Math.Cos(Math.PI * seconds / 7.5);
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
        if (_menuArtPanel.Effect is DropShadowEffect glow)
        {
            double pulse = (1 - Math.Cos(Math.PI * seconds / 2.4)) / 2;
            glow.BlurRadius = 16 + 18 * pulse;
            glow.Opacity = .55 + .4 * pulse;
        }
    }
}
