using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AssetsTabView
{
    private bool _officialNoteLanded;
    private IDisposable? _officialVisibility;
    private CancellationTokenSource? _officialLanding;

    // Test seam observes the actual animation and cancellation without a wall-clock delay.
    internal Func<Animation, CancellationToken, Task>? RunOfficialLanding;

    private void InitializeOfficialNoteLanding()
    {
        OnlineOfficialNote.AttachedToVisualTree += (_, _) =>
        {
            _officialVisibility?.Dispose();
            _officialVisibility = EffectiveVisibility.Watch(OnlineOfficialNote, RefreshOfficialLanding);
        };
        OnlineOfficialNote.Loaded += (_, _) => RefreshOfficialLanding();
        OnlineOfficialNote.DetachedFromVisualTree += (_, _) =>
        {
            _officialVisibility?.Dispose();
            _officialVisibility = null;
            StopOfficialLanding();
        };
    }

    private void RefreshOfficialLanding()
    {
        if (!OnlineOfficialNote.IsEffectivelyVisible) { StopOfficialLanding(); return; }
        if (!OnlineOfficialNote.IsLoaded || _officialNoteLanded) return;
        _officialNoteLanded = true;
        LandOfficialNote();
    }

    private void StopOfficialLanding() => _officialLanding?.Cancel();

    // WPF MainWindow.OnlineMeter.cs:100-102,440-454: first on-screen only, 420 ms,
    // cubic out, opacity .2 -> 1; Full also slides -6 -> 0. Gentle caps Full to Reduced.
    // Reuses the finite Visual animation from lane 5d45af7f3dd3, not a transform clock.
    private async void LandOfficialNote()
    {
        var level = AmbientFxCanvas.Env.Level;
        if (level == MotionLevel.Off) return;
        using var landing = new CancellationTokenSource();
        _officialLanding = landing;
        var animation = OfficialNoteEntrance(level);
        try
        {
            if (RunOfficialLanding is { } run) await run(animation, landing.Token);
            else await animation.RunAsync(OnlineOfficialNote, landing.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug(ex, "Official note landing failed"); }
        finally { if (ReferenceEquals(_officialLanding, landing)) _officialLanding = null; }
    }
    internal static Animation OfficialNoteEntrance(MotionLevel level) => new Animation
        {
            Duration = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(420)),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters =
                {
                    new Setter(OpacityProperty, 0.2),
                    new Setter(RenderTransformProperty, TransformOperations.Parse(
                        level == MotionLevel.Full ? "translate(0px, -6px)" : "translate(0px, 0px)")),
                } },
                new KeyFrame { Cue = new Cue(1), Setters =
                {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(RenderTransformProperty, TransformOperations.Parse("translate(0px, 0px)")),
                } },
            },
        };
}
