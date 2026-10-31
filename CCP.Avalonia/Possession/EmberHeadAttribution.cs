using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;
namespace ConditioningControlPanel.Avalonia.Possession;
internal sealed class EmberHeadAttribution(Canvas layer, Func<Control, Rect?> bounds,
    IFrameSource frames, Func<bool> photosafe) : IPossessionAttribution
{
    private sealed class Mark(Control target, Border glass, bool held)
    {
        public readonly Control Target = target;
        public readonly Border Glass = glass;
        public readonly bool Held = held;
        public TimeSpan? Start;
        public double Duration;
        public bool Static;
        public TaskCompletionSource Done = new();
        public CancellationTokenRegistration Cancellation;
    }
    private readonly List<Mark> _marks = new();
    private bool _subscribed;
    private static readonly Color Ember = Color.Parse("#FF8A5C");
    public bool AnyPossessed => _marks.Exists(m => m.Held);
    public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400)
    {
        ct.ThrowIfCancellationRequested();
        if (target is not Control control) return Task.CompletedTask;
        var mark = Add(control, false);
        mark.Duration = mark.Static ? Math.Max(600, durationMs) : Math.Max(80, durationMs);
        mark.Cancellation = ct.Register(() =>
        {
            void Cancel() { Remove(mark); mark.Done.TrySetCanceled(ct); }
            if (Dispatcher.UIThread.CheckAccess()) Cancel(); else Dispatcher.UIThread.Post(Cancel);
        });
        return mark.Done.Task;
    }
    public IDisposable Possess(object target)
    {
        if (target is not Control c) return new Release(() => { });
        var mark = Add(c, true);
        return new Release(() => Remove(mark));
    }
    private Mark Add(Control target, bool held)
    {
        bool still = !held && photosafe();
        var glass = new Border
        {
            BorderBrush = new SolidColorBrush(held ? Color.FromArgb(191, Ember.R, Ember.G, Ember.B) : Ember), BorderThickness = new Thickness(held ? 1 : 2),
            Background = new SolidColorBrush(Color.FromArgb(held ? (byte)20 : (byte)31, Ember.R, Ember.G, Ember.B)),
            CornerRadius = new CornerRadius(held ? 6 : 8), IsHitTestVisible = false, Opacity = held || still ? 1 : 0,
            BoxShadow = !held && !still ? new BoxShadows(new BoxShadow { Blur = 18, Color = Ember }) : default,
        };
        var mark = new Mark(target, glass, held) { Static = still };
        _marks.Add(mark); layer.Children.Add(glass); Place(mark);
        if (!_subscribed) { _subscribed = true; frames.Subscribe(OnFrame); }
        return mark;
    }
    private void Place(Mark mark)
    {
        if (bounds(mark.Target) is not { } r) return;
        Canvas.SetLeft(mark.Glass, r.X); Canvas.SetTop(mark.Glass, r.Y);
        mark.Glass.Width = r.Width; mark.Glass.Height = r.Height;
    }
    private void OnFrame(TimeSpan now)
    {
        foreach (var mark in _marks.ToArray())
        {
            Place(mark);
            if (mark.Held) continue;
            mark.Start ??= now;
            double p = Math.Clamp((now - mark.Start.Value).TotalMilliseconds / mark.Duration, 0, 1);
            if (p >= 1) { Remove(mark); mark.Done.TrySetResult(); continue; }
            if (!mark.Static)
            {
                double q = p < 0.35 ? p / 0.35 : (p - 0.35) / 0.65;
                mark.Glass.Opacity = p < 0.35 ? 0.95 * (1 - Math.Pow(1 - q, 2)) : 0.95 * Math.Pow(1 - q, 2);
            }
        }
    }
    private void Remove(Mark mark)
    {
        if (!_marks.Remove(mark)) return;
        layer.Children.Remove(mark.Glass); mark.Cancellation.Dispose();
        if (_marks.Count == 0 && _subscribed) { _subscribed = false; frames.Unsubscribe(OnFrame); }
    }
    public void EdgePulse(double strength) { }
    public void ReleaseAll()
    {
        foreach (var mark in _marks.ToArray()) { Remove(mark); mark.Done.TrySetCanceled(); }
    }
    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
