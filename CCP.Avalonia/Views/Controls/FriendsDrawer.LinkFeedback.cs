using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private readonly Canvas _linkFx = new() { IsHitTestVisible = false, Tag = "friends-link-fx" };
    private readonly List<(Ellipse Dot, double X, double Y, double Ms)> _linkSparks = new();
    private DispatcherTimer? _linkPulseTimer;
    private ScaleTransform? _linkScale;
    private double _linkAmount;

    // WPF FriendsDrawer.Juice.cs Pop/Sparks: 110 ms peak, 380 ms settle, 12/6/0 sparks.
    internal void StartLinkFeedback(Control pill)
    {
        AdvanceLinkFeedback(680);
        _linkAmount = AmbientFxCanvas.Env.Level switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };
        if (_linkAmount == 0) return;
        pill.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        pill.RenderTransform = _linkScale = new ScaleTransform(1, 1);
        if (pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), _linkFx) is { } center)
            for (int i = 0; i < (int)Math.Round(12 * _linkAmount); i++)
            {
                double size = 3 + Random.Shared.NextDouble() * 3, angle = Random.Shared.NextDouble() * Math.PI * 2;
                double distance = (24 + Random.Shared.NextDouble() * 30) * _linkAmount;
                var dot = new Ellipse { Width = size, Height = size, Fill = Lilac,
                    Effect = new DropShadowEffect { Color = Color.FromRgb(0xB9, 0x9C, 0xFF), BlurRadius = 6, Opacity = 0.9, OffsetX = 0, OffsetY = 0 },
                    RenderTransform = new TranslateTransform() };
                Canvas.SetLeft(dot, center.X - size / 2); Canvas.SetTop(dot, center.Y - size / 2);
                _linkFx.Children.Add(dot);
                _linkSparks.Add((dot, Math.Cos(angle) * distance, Math.Sin(angle) * distance + 6, 420 + Random.Shared.Next(260)));
            }
        var clock = Stopwatch.StartNew();
        _linkPulseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (_, _) => AdvanceLinkFeedback(clock.Elapsed.TotalMilliseconds));
    }

    // The same clock path is driven explicitly by headless tests, without wall-clock sleeps.
    internal void AdvanceLinkFeedback(double ms)
    {
        if (_linkScale != null)
        {
            double pulse;
            if (ms <= 110) { double t = Math.Clamp(ms / 110, 0, 1); pulse = 1 - (1 - t) * (1 - t); }
            else
            {
                // WPF ElasticEase EaseOut, Oscillations=1, Springiness=4, ending at 380 ms.
                double t = 1 - Math.Clamp((ms - 110) / 270, 0, 1);
                pulse = (Math.Exp(4 * t) - 1) / (Math.Exp(4) - 1) * Math.Sin(2.5 * Math.PI * t);
            }
            _linkScale.ScaleX = _linkScale.ScaleY = 1 + 0.22 * _linkAmount * pulse;
        }
        foreach (var (dot, x, y, duration) in _linkSparks)
        {
            double t = Math.Clamp(ms / duration, 0, 1), eased = 1 - (1 - t) * (1 - t);
            var move = (TranslateTransform)dot.RenderTransform!;
            move.X = x * eased; move.Y = y * eased; dot.Opacity = 1 - t;
            if (t == 1) _linkFx.Children.Remove(dot);
        }
        if (ms < 680) return;
        _linkPulseTimer?.Stop(); _linkPulseTimer = null;
        _linkScale = null; _linkSparks.Clear(); _linkFx.Children.Clear();
    }
}
