// WPF: ConditioningControlPanel/Services/UI/ScreenShakeService.cs:22
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed class ScreenShakeService
{
    internal static readonly ScreenShakeService Instance = new();
    private readonly List<(Control Root, ITransform? Prior, TranslateTransform Shake)> _targets = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _endsAt;
    private double _amplitude;

    internal ScreenShakeService() => _timer.Tick += (_, _) => Tick();

    internal void Shake(double intensity, int durationMs)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        if (double.IsNaN(intensity) || intensity <= 0 || durationMs <= 0) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Shake(intensity, durationMs));
            return;
        }
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (desktop == null) return;
        Start(desktop.Windows, intensity, durationMs);
    }

    internal void Start(IEnumerable<Window> windows, double intensity, int durationMs)
    {
        Reset();
        _amplitude = 28 * Math.Clamp(intensity, 0, 1);
        if (double.IsNaN(_amplitude) || _amplitude <= 0 || durationMs <= 0) return;
        foreach (var window in windows)
        {
            if (!window.IsVisible || window.Content is not Control root) continue;
            var shake = new TranslateTransform();
            _targets.Add((root, root.RenderTransform, shake));
            root.RenderTransform = shake;
        }
        _endsAt = DateTime.UtcNow.AddMilliseconds(durationMs);
        if (_targets.Count > 0) { Tick(); _timer.Start(); }
    }

    internal void Tick()
    {
        if (DateTime.UtcNow >= _endsAt) { Reset(); return; }
        var x = (Random.Shared.NextDouble() * 2 - 1) * _amplitude;
        var y = (Random.Shared.NextDouble() * 2 - 1) * _amplitude;
        foreach (var target in _targets) { target.Shake.X = x; target.Shake.Y = y; }
    }

    internal void Reset()
    {
        _timer.Stop();
        foreach (var target in _targets)
            if (ReferenceEquals(target.Root.RenderTransform, target.Shake))
                target.Root.RenderTransform = target.Prior;
        _targets.Clear();
    }
}
