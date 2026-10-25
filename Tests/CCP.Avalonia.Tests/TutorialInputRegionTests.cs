using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Drives the live overlay the way a tour does (P04): the first card is read on Loaded
/// from CoreTutorial.CurrentStep, every later card arrives through CoreTutorial.StepChanged, and
/// UpdateStep alone picks clickThroughHole / BlockBackgroundClicks from real step data.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TutorialInputRegionTests
{
    [Fact]
    public Task SpotlightLetsTargetThroughBlocksDimAndManualStepRestoresAllInput() => Run((overlay, target) =>
    {
        // First card (read on Loaded): an OnButtonClick step lets the click reach its target.
        Assert.NotNull(overlay.InputRectangles);
        var (x, y) = Pixel(overlay, target, target.Bounds.Width / 2, target.Bounds.Height / 2);
        Assert.False(Hits(overlay, x, y)); // the WPF target rect, not the padded visual glow
        var (lx, _) = Pixel(overlay, target, -1, 0);
        Assert.True(Hits(overlay, lx, y));
        var (rx, _) = Pixel(overlay, target, target.Bounds.Width, 0);
        Assert.True(Hits(overlay, rx, y)); // half-open right edge

        Step(Spot(TutorialAdvance.Manual));
        Assert.Null(overlay.InputRectangles);
        Step(new CoreTutorial.Step { Id = "c", TextPosition = CoreTutorial.StepPosition.Center });
        Assert.Null(overlay.InputRectangles);
    });

    [Fact]
    public Task TransparentWaitLetsBackgroundThroughAndKeepsSkipButtonsClickable() => Run((overlay, _) =>
    {
        Step(new CoreTutorial.Step
        {
            Id = "wait", TextPosition = CoreTutorial.StepPosition.Center, BlockBackgroundClicks = false,
        });
        overlay.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var card = overlay.FindControl<Border>("TextPanel")!;
        var (cx, cy) = Pixel(overlay, card, 10, 10);
        Assert.NotNull(overlay.InputRectangles);
        Assert.Single(overlay.InputRectangles!);
        Assert.True(Hits(overlay, cx, cy));
        Assert.False(Hits(overlay, 0, 0));
        Step(new CoreTutorial.Step { Id = "c", TextPosition = CoreTutorial.StepPosition.Center });
        Assert.Null(overlay.InputRectangles);
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

    private static (int X, int Y) Pixel(TutorialOverlay overlay, Visual v, double dx, double dy)
    {
        var p = overlay.PointToClient(v.PointToScreen(new Point(dx, dy)));
        return ((int)Math.Floor(p.X * overlay.RenderScaling), (int)Math.Floor(p.Y * overlay.RenderScaling));
    }

    private static bool Hits(TutorialOverlay overlay, int x, int y)
        => Win32Overlay.Hits(overlay.InputRectangles!, overlay.InputRectangles!.Length, x, y);

    private static Task Run(Action<TutorialOverlay, Control> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var savedProvider = CoreTutorial.CurrentStepProvider;
        var target = new Button { Name = "TourTarget", Width = 200, Height = 50 };
        Canvas.SetLeft(target, 150);
        Canvas.SetTop(target, 120);
        var window = new Window { Width = 800, Height = 600, Content = new Canvas { Children = { target } } };
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
            body(overlay, target);
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
