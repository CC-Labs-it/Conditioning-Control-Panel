using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;

namespace ConditioningControlPanel.Avalonia.Theme;

// WPF Controls.xaml:272-277,308-314: SnapshotAndReplace keeps the visible spark
// when ON's 450ms decay is interrupted by OFF's 80ms fade.
public sealed class ToggleSpark : AvaloniaObject
{
    public static readonly AttachedProperty<bool?> IsOnProperty =
        AvaloniaProperty.RegisterAttached<ToggleSpark, Ellipse, bool?>("IsOn");
    private static readonly AttachedProperty<CancellationTokenSource?> RunProperty =
        AvaloniaProperty.RegisterAttached<ToggleSpark, Ellipse, CancellationTokenSource?>("Run");

    public static bool? GetIsOn(Ellipse target) => target.GetValue(IsOnProperty);
    public static void SetIsOn(Ellipse target, bool? value) => target.SetValue(IsOnProperty, value);

    static ToggleSpark() => IsOnProperty.Changed.AddClassHandler<Ellipse>((target, change) =>
    {
        double visible = target.Opacity;
        Stop(target);
        target.DetachedFromVisualTree -= Detached;
        target.DetachedFromVisualTree += Detached;
        var run = new CancellationTokenSource();
        target.SetValue(RunProperty, run);
        bool on = GetIsOn(target) == true;
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(on ? 450 : 80) };
        animation.Children.Add(Frame(0, on ? 0 : visible));
        if (on) animation.Children.Add(Frame(60.0 / 450, 0.9));
        animation.Children.Add(Frame(1, 0));
        _ = animation.RunAsync(target, run.Token);
    });

    private static KeyFrame Frame(double cue, double opacity) => new()
    {
        Cue = new Cue(cue), Setters = { new Setter(Visual.OpacityProperty, opacity) }
    };

    private static void Stop(Ellipse target)
    {
        var run = target.GetValue(RunProperty);
        target.ClearValue(RunProperty);
        run?.Cancel();
        run?.Dispose();
    }

    private static void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var target = (Ellipse)sender!;
        Stop(target);
        target.DetachedFromVisualTree -= Detached;
    }
}
