// WPF Services/Possession/Effects/ToastEffect.cs:23-239.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class ToastEffect : PossessionEffectBase
{
    private const double Width = 300, Margin = 18, SlideMs = 280;
    private static string? _lastLine;
    private Canvas? _layer;
    private Window? _window;
    private Border? _toast;
    private TranslateTransform? _slide;
    private IFrameSource? _frames;
    private Rect _rect;
    private TimeSpan? _start;
    private bool _returning;
    private double _duration, _fromX, _fromOpacity;
    private TaskCompletionSource? _finished, _entered;
    public override string Id => "toast";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);
    protected override bool ChargeOnApply => false;
    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { GhostLayer: not null };

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer) return;
        var line = ToastLines.Pick(ctx.Rng, _lastLine); _lastLine = line;
        var face = Views.AvatarTube.AvatarTubeWindow.Live?.FindControl<Image>("ImgAvatar")?.Source;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Control portrait = face != null
            ? new Border { Width = 30, Height = 38, CornerRadius = new CornerRadius(4), ClipToBounds = true,
                Background = new SolidColorBrush(Color.Parse("#DC143C")), Child = new Image { Source = face, Stretch = Stretch.UniformToFill } }
            : new TextBlock { Text = "◆", Foreground = new SolidColorBrush(Color.Parse("#DC143C")), FontSize = 15, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        row.Children.Add(portrait);
        var text = new TextBlock { Text = line, Foreground = new SolidColorBrush(Color.Parse("#FFE3DA")), FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var toast = new Border { Width = Width, Background = new SolidColorBrush(Color.Parse("#1A0A0A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#DC143C")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 12, 8), IsHitTestVisible = false, Child = row };
        toast.Measure(new Size(Width, double.PositiveInfinity));
        double height = Math.Max(44, toast.DesiredSize.Height);
        double w = layer.Bounds.Width, h = layer.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double left = Math.Max(Margin, w - Width - Margin), top = Math.Max(Margin, h - height - Margin);
        _rect = new Rect(left, top, Width, height);
        Canvas.SetLeft(toast, left); Canvas.SetTop(toast, top);
        _layer = layer; _toast = toast; _window = host.PossessionWindow; _frames = host.PossessionFrames;
        _slide = new TranslateTransform(Width + Margin, 0); toast.RenderTransform = _slide; toast.Opacity = 0;
        layer.Children.Add(toast);
        _window?.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        var entered = _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _frames.Subscribe(Frame);
        try
        {
            // WPF waits 280 ms plus 20 ms before the director starts its hold.
            await Task.WhenAll(entered.Task, Task.Delay(300, ct)).WaitAsync(TimeSpan.FromSeconds(1), ct);
        }
        catch (TimeoutException) { await UndoAsync(TimeSpan.Zero); }
    }
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (_layer != null && _toast != null && _rect.Contains(e.GetPosition(_layer)))
        {
            if (e.GetCurrentPoint(_layer).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                e.Handled = true;
            _ = UndoAsync(TimeSpan.FromMilliseconds(160));
        }
    }
    private void Frame(TimeSpan now)
    {
        if (_toast == null || _slide == null) return;
        _start ??= now;
        double t = Math.Clamp((now - _start.Value).TotalMilliseconds / (_returning ? _duration : SlideMs), 0, 1);
        double k = _returning ? t * t * t : 1 - Math.Pow(1 - t, 3);
        _slide.X = _returning ? _fromX + (Width + Margin - _fromX) * k : (Width + Margin) * (1 - k);
        _toast.Opacity = _returning ? _fromOpacity * (1 - k) : k;
        if (t >= 1) { if (_returning) Finish(); else _entered?.TrySetResult(); }
    }
    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        if (_toast == null || duration <= TimeSpan.Zero) { Finish(); return; }
        _window?.RemoveHandler(InputElement.PointerPressedEvent, Press);
        _returning = true; _start = null; _duration = Math.Clamp(duration.TotalMilliseconds, 160, SlideMs);
        _fromX = _slide?.X ?? 0; _fromOpacity = _toast.Opacity;
        var done = _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (await Task.WhenAny(done.Task, Task.Delay((int)_duration + 100)).ConfigureAwait(true) != done.Task) Finish();
        await done.Task.ConfigureAwait(true);
    }
    private void Finish()
    {
        _window?.RemoveHandler(InputElement.PointerPressedEvent, Press);
        _frames?.Unsubscribe(Frame);
        if (_toast != null) _layer?.Children.Remove(_toast);
        _toast = null; _slide = null; _layer = null; _window = null; _frames = null;
        _start = null; _returning = false; _entered = null; _finished?.TrySetResult(); _finished = null;
    }
}
