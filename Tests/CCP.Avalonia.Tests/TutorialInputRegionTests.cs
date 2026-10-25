using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Drives the live overlay the way a tour does (P04): the first card is read on Loaded
/// from CoreTutorial.CurrentStep, every later card arrives through CoreTutorial.StepChanged, and
/// UpdateStep alone picks clickThroughHole / BlockBackgroundClicks from real step data.</summary>
/// <summary>Drives the live overlay the way a tour does (P04): the first card is read on Show
/// from CoreTutorial.CurrentStep, every later card arrives through CoreTutorial.StepChanged, and
/// UpdateStep alone picks clickThroughHole / BlockBackgroundClicks from real step data. The live
/// card sits in the host's OverlayLayer (no OS input region), so input is proven with real
/// pointer clicks on the host: a control under the dim, the target in the hole, the card's buttons.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TutorialInputRegionTests
{
    [Fact]
    public Task SpotlightLetsTargetThroughBlocksDimAndManualStepRestoresAllInput() => Run((host, overlay, target, behind) =>
    {
        int targetClicks = 0, behindClicks = 0;
        target.Click += (_, _) => targetClicks++;
        behind.Click += (_, _) => behindClicks++;
        var y = target.Bounds.Height / 2;

        // First card (read on Show): an OnButtonClick step. Only the WPF target rect is a hole,
        // not the padded visual glow: just left of the target and at its half-open right edge
        // the control behind stays unreachable.
        Click(host, target, -1, y);
        Click(host, target, target.Bounds.Width, y);
        Click(host, behind, 60, 300);
        Assert.Equal(0, targetClicks);
        Assert.Equal(0, behindClicks);
        // The first card's hole lets the click reach its target.
        Click(host, target, target.Bounds.Width / 2, y);
        Assert.Equal(1, targetClicks);

        // Manual spotlight and a centred card: the whole host is dimmed and swallows clicks.
        Step(Spot(TutorialAdvance.Manual));
        Click(host, target, target.Bounds.Width / 2, y);
        Assert.Equal(1, targetClicks);
        Step(new CoreTutorial.Step { Id = "c", TextPosition = CoreTutorial.StepPosition.Center });
        Click(host, target, target.Bounds.Width / 2, y);
        Click(host, behind, 60, 300);
        Assert.Equal(1, targetClicks);
        Assert.Equal(0, behindClicks);

        // Back to a click-through spotlight: the target itself is reachable.
        Step(Spot(TutorialAdvance.Click));
        Click(host, target, target.Bounds.Width / 2, y);
        Assert.Equal(2, targetClicks);
        Assert.Equal(0, behindClicks);
    });

    [Fact]
    public Task TransparentWaitLetsBackgroundThroughAndKeepsSkipButtonsClickable() => Run((host, overlay, _, behind) =>
    {
        var behindClicks = 0;
        behind.Click += (_, _) => behindClicks++;
        Step(new CoreTutorial.Step
        {
            Id = "wait", TextPosition = CoreTutorial.StepPosition.Center, BlockBackgroundClicks = false,
            AllowManualSkip = true,
        });
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Click(host, behind, 60, 300);
        Assert.Equal(1, behindClicks);

        var skip = overlay.StageRoot!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BtnSkipStep");
        Assert.True(skip.IsEffectivelyVisible);
        var skipClicks = 0;
        skip.Click += (_, _) => skipClicks++;
        Click(host, skip, skip.Bounds.Width / 2, skip.Bounds.Height / 2);
        Assert.Equal(1, skipClicks);
        Assert.Equal(1, behindClicks);

        Step(new CoreTutorial.Step { Id = "c", TextPosition = CoreTutorial.StepPosition.Center });
        Click(host, behind, 60, 300);
        Assert.Equal(1, behindClicks);
    });

    private enum TutorialAdvance { Manual, Click }

    private static CoreTutorial.Step Spot(TutorialAdvance advance) => new()
    {
        Id = "spot", Title = "T", Icon = "~",
        TargetElementName = "TourTarget",
        TextPosition = CoreTutorial.StepPosition.Bottom,
        Advance = advance == TutorialAdvance.Manual
            ? CoreTutorial.AdvanceTrigger.Manual
            : CoreTutorial.AdvanceTrigger.OnButtonClick,
    };

    private static CoreTutorial.Step? _current;

    private static void Step(CoreTutorial.Step step)
    {
        _current = step;
        CoreTutorial.RaiseStepChanged(null, step);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window host, Visual v, double dx, double dy)
    {
        // Pointer hit testing reads the last rendered frame: render the current card first.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var p = v.TranslatePoint(new Point(dx, dy), host)!.Value;
        host.MouseDown(p, MouseButton.Left);
        host.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(Action<Window, TutorialOverlay, Button, Button> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var savedProvider = CoreTutorial.CurrentStepProvider;
        var target = new Button { Name = "TourTarget", Width = 200, Height = 50 };
        Canvas.SetLeft(target, 150);
        Canvas.SetTop(target, 120);
        var behind = new Button { Name = "Behind", Width = 800, Height = 600 };
        var window = new Window { Width = 800, Height = 600, Content = new Canvas { Children = { behind, target } } };
        // A XAML window carries a name scope; FindControl needs one, as on the real shell.
        var scope = new NameScope();
        scope.Register(target.Name!, target);
        NameScope.SetNameScope(window, scope);
        TutorialOverlay? overlay = null;
        try
        {
            _current = Spot(TutorialAdvance.Click);
            CoreTutorial.CurrentStepProvider = () => _current;
            window.Show();
            window.UpdateLayout();
            overlay = new TutorialOverlay(window);
            overlay.Show();
            overlay.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(overlay.IsOnStage);
            body(window, overlay, target, behind);
        }
        finally
        {
            overlay?.Close();
            window.Close();
            CoreTutorial.CurrentStepProvider = savedProvider;
            _current = null;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
