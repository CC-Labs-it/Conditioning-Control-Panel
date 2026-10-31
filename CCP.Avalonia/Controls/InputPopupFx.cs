using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>WPF Inputs.xaml:121 Slide: restart on open, cancel on close/detach.
/// Configured 150ms; native compositor timing is not measured.</summary>
public static class InputPopupFx
{
    public static readonly AttachedProperty<bool> SlideProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("Slide", typeof(InputPopupFx));
    private static readonly ConditionalWeakTable<Popup, CancellationTokenSource> Runs = new();
    public static bool GetSlide(Popup popup) => popup.GetValue(SlideProperty);
    public static void SetSlide(Popup popup, bool value) => popup.SetValue(SlideProperty, value);

    static InputPopupFx()
    {
        SlideProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            popup.Opened -= Opened;
            popup.Closed -= Closed;
            popup.DetachedFromVisualTree -= Detached;
            Stop(popup);
            if (e.NewValue is true)
            {
                popup.Opened += Opened;
                popup.Closed += Closed;
                popup.DetachedFromVisualTree += Detached;
                if (popup.IsOpen) Opened(popup, EventArgs.Empty);
            }
        });
    }

    internal static bool IsSliding(Popup popup) => Runs.TryGetValue(popup, out _);
    internal static Animation CreateSlideAnimation(double height) => new()
    {
        Duration = TimeSpan.FromMilliseconds(150),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new global::Avalonia.Styling.Setter(TranslateTransform.YProperty, -height) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new global::Avalonia.Styling.Setter(TranslateTransform.YProperty, 0d) } },
        },
    };

    private static async void Opened(object? sender, EventArgs e)
    {
        var popup = (Popup)sender!; Stop(popup);
        if (popup.Child is not { } child) return;
        var run = new CancellationTokenSource();
        Runs.Add(popup, run);
        try { await CreateSlideAnimation(child.Bounds.Height).RunAsync(child, run.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            if (Runs.TryGetValue(popup, out var current) && ReferenceEquals(current, run))
                Runs.Remove(popup);
            run.Dispose();
        }
    }

    private static void Closed(object? sender, EventArgs e) => Stop((Popup)sender!);
    private static void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Stop((Popup)sender!);
    private static void Stop(Popup popup)
    {
        if (!Runs.TryGetValue(popup, out var run)) return;
        Runs.Remove(popup); run.Cancel();
    }
}
