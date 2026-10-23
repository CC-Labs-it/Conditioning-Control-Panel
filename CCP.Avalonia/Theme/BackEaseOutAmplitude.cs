using Avalonia.Animation.Easings;

namespace ConditioningControlPanel.Avalonia.Theme;

/// <summary>
/// WPF <c>BackEase { EasingMode = EaseOut, Amplitude = a }</c>. Avalonia's <c>BackEaseOut</c> has no
/// amplitude (it is WPF's default 1.0), which overshoots a button press release about three times
/// harder than WPF's 0.3 (Controls.xaml:77). Same arithmetic as WPF: f(t) = t^3 - a*t*sin(pi*t), mirrored.
/// </summary>
public sealed class BackEaseOutAmplitude : Easing
{
    public double Amplitude { get; set; } = 1.0;

    public override double Ease(double progress)
    {
        double t = 1 - progress;
        return 1 - (t * t * t - Amplitude * t * System.Math.Sin(System.Math.PI * t));
    }
}
