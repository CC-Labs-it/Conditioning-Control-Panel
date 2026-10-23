// WPF EmberAttribution: charge before motion, following outlines, cursor ring and edge pulse.
// Trunk a4ee52d40: ember BoxShadow adapted; our frame clock and Core contracts win.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class PossessionAttribution : IPossessionAttribution
{
    private readonly PossessionHost _host;
    private readonly List<Cue> _cues = new();
    private readonly Dictionary<Border, Control> _outlines = new();
    private readonly Ellipse _ring = new() { Width = 22, Height = 22, Stroke = Ember, StrokeThickness = 1.5, Opacity = 0, IsHitTestVisible = false };
    private bool _subscribed;
    private TimeSpan? _followAt, _ringAt;
    private double _ringFrom, _ringTo;
    private static readonly IBrush Ember = new SolidColorBrush(Color.Parse("#FF8A5C"));
    private static readonly IBrush Tint = new SolidColorBrush(Color.Parse("#1FFF8A5C"));
    private bool Still => GentleVisuals.Photosafe(CoreSettings.Current.LockdownPhotosafe);
    internal PossessionAttribution(PossessionHost host) => _host = host;
    private sealed class Cue
    {
        public required Border Border;
        public required double Duration, Peak, Apex;
        public bool Static, Pulse;
        public TimeSpan? Start;
        public readonly TaskCompletionSource Done = new();
        public CancellationTokenRegistration Registration;
    }
    private Border NewBorder() => new() { BorderBrush = Ember, Background = Tint, BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
    private void Place(Border b, Control c)
    {
        var r = _host.BoundsOf(c);
        b.IsVisible = c.IsEffectivelyVisible && r.HasValue;
        if (r is not { } bounds) return;
        b.Width = bounds.Width; b.Height = bounds.Height;
        Canvas.SetLeft(b, bounds.X); Canvas.SetTop(b, bounds.Y);
    }
    private void Listen()
    {
        if (_subscribed) return;
        _subscribed = true; _host.Frames.Subscribe(OnFrame);
    }
    public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400)
    {
        ct.ThrowIfCancellationRequested();
        if (target is not Control c || !c.IsEffectivelyVisible || _host.BoundsOf(c) is not { Width: > 1, Height: > 1 }) return Task.CompletedTask;
        bool still = Still;
        var b = NewBorder(); Place(b, c); b.Opacity = still ? 1 : 0;
        // ponytail: BoxShadow supplies the ripple glow without an animated Effect over the shell.
        if (!still) b.BoxShadow = BoxShadows.Parse("0 0 18 0 #88FF8A5C");
        var cue = new Cue { Border = b, Duration = Math.Max(still ? 600 : 80, durationMs), Peak = 0.95, Apex = 0.35, Static = still };
        _host.GhostLayer.Children.Add(b); _cues.Add(cue); Listen();
        cue.Registration = ct.Register(() =>
        {
            void Cancel() { Finish(cue, true); }
            if (Dispatcher.UIThread.CheckAccess()) Cancel(); else Dispatcher.UIThread.Post(Cancel);
        });
        return cue.Done.Task;
    }
    public bool AnyPossessed => _outlines.Count > 0;
    public IDisposable Possess(object target)
    {
        if (target is not Control c) return new Handle(() => { });
        var b = NewBorder(); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(6);
        b.BorderBrush = new SolidColorBrush(Color.Parse("#BFFF8A5C")); b.Background = new SolidColorBrush(Color.Parse("#14FF8A5C")); Place(b, c);
        _outlines.Add(b, c); _host.GhostLayer.Children.Add(b);
        if (!_host.GhostLayer.Children.Contains(_ring)) _host.GhostLayer.Children.Add(_ring);
        Listen();
        return new Handle(() =>
        {
            _outlines.Remove(b); _host.GhostLayer.Children.Remove(b);
            if (!AnyPossessed && _ring.Opacity <= 0) DropRing();
            Park();
        });
    }
    public void EdgePulse(double strength)
    {
        if (strength <= 0 || _cues.Count(c => c.Pulse) >= 3) return;
        var b = NewBorder(); b.Background = null; b.CornerRadius = default; b.BorderThickness = new Thickness(10); b.Opacity = 0;
        b.Width = _host.GhostLayer.Bounds.Width; b.Height = _host.GhostLayer.Bounds.Height;
        if (b.Width <= 0 || b.Height <= 0) return;
        b.BoxShadow = BoxShadows.Parse("inset 0 0 24 0 #88FF8A5C");
        _cues.Add(new Cue { Border = b, Duration = Still ? 900 : 500, Peak = Math.Clamp(strength, 0, Still ? 0.5 : 1), Apex = 0.4, Pulse = true });
        _host.GhostLayer.Children.Add(b); Listen();
    }
    private void OnFrame(TimeSpan now)
    {
        foreach (var cue in _cues.ToArray())
        {
            cue.Start ??= now;
            double f = (now - cue.Start.Value).TotalMilliseconds / cue.Duration;
            if (f >= 1) { Finish(cue, false); continue; }
            double Ease(double x) => 1 - (1 - x) * (1 - x);
            cue.Border.Opacity = cue.Static ? 1 : Math.Clamp(f < cue.Apex ? cue.Peak * Ease(f / cue.Apex)
                : cue.Peak * (1 - Ease((f - cue.Apex) / (1 - cue.Apex))), 0, 1);
        }
        if (_followAt == null || now - _followAt >= TimeSpan.FromMilliseconds(100))
        {
            foreach (var pair in _outlines) Place(pair.Key, pair.Value);
            _followAt = now;
        }
        double to = AnyPossessed && _host.PointerInside ? 1 : 0;
        if (to != _ringTo) { _ringFrom = _ring.Opacity; _ringTo = to; _ringAt = now; }
        double fade = _ringAt.HasValue ? Math.Clamp((now - _ringAt.Value).TotalMilliseconds / 250, 0, 1) : 1;
        _ring.Opacity = Math.Clamp(_ringFrom + (_ringTo - _ringFrom) * fade, 0, 1);
        _ring.IsVisible = _ring.Opacity > 0;
        Canvas.SetLeft(_ring, _host.Pointer.X - 11); Canvas.SetTop(_ring, _host.Pointer.Y - 11);
        if (!AnyPossessed && _ring.Opacity <= 0) DropRing();
        Park();
    }
    private void Finish(Cue cue, bool cancel)
    {
        if (!_cues.Remove(cue)) return;
        _host.GhostLayer.Children.Remove(cue.Border); cue.Registration.Dispose();
        if (cancel) cue.Done.TrySetCanceled(); else cue.Done.TrySetResult();
        Park();
    }
    private void Park()
    {
        if (!_subscribed || _cues.Count > 0 || AnyPossessed || _ring.Opacity > 0) return;
        _subscribed = false; _host.Frames.Unsubscribe(OnFrame);
    }
    private void DropRing()
    {
        _host.GhostLayer.Children.Remove(_ring); _ring.Opacity = _ringFrom = _ringTo = 0; _ringAt = null;
    }
    public void ReleaseAll()
    {
        foreach (var cue in _cues.ToArray()) Finish(cue, true);
        foreach (var b in _outlines.Keys) _host.GhostLayer.Children.Remove(b);
        _outlines.Clear(); DropRing(); _followAt = null; Park();
    }
    private sealed class Handle(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var action = _release; _release = null; action?.Invoke(); }
    }
}
