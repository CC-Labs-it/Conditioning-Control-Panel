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
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialStageTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    private static Task WithHost(Action<Window, TutorialOverlay, Button, Button, int> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldStore = TutorialService.CompletionStore;
        var oldShell = TutorialHeadHooks.ShellProvider;
        TutorialService.CompletionStore = new Store();
        TutorialHeadHooks.ShellProvider = () => null;
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var host = new Window { Width = 900, Height = 650 };
        TutorialOverlay? overlay = null;
        TutorialStep? step = null;
        string? oldWindow = null;
        try
        {
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (TutorialHead.Service.CurrentStep!.Id != "iht_play") TutorialHead.Service.Next();
            step = TutorialHead.Service.CurrentStep;
            oldWindow = step.TargetWindowTypeName;
            step.TargetWindowTypeName = null; // fixture isolates the control interaction from dialog switching
            var target = new Button { Name = step.TargetElementName, Content = "Target", Width = 160, Height = 42 };
            var outside = new Button { Content = "Outside", Width = 100, Height = 42 };
            Canvas.SetLeft(target, 40); Canvas.SetTop(target, 60);
            Canvas.SetLeft(outside, 700); Canvas.SetTop(outside, 550);
            host.Content = new Canvas { Children = { target, outside } };
            host.Show();
            overlay = new TutorialOverlay(host);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            check(host, overlay, target, outside, CoreTutorial.CurrentStepIndex);
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            host.Close();
            if (step != null) step.TargetWindowTypeName = oldWindow;
            TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void Click(Window host, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        host.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task LiveCardLivesInsideHostAndPointerHoleReachesOnlyTheHighlightedControl() => WithHost((host, overlay, target, outside, index) =>
    {
        Assert.False(overlay.IsVisible);
        Assert.Contains(host.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
        var clicked = 0; var blocked = 0;
        target.Click += (_, _) => clicked++;
        outside.Click += (_, _) => blocked++;
        Click(host, outside);
        Assert.Equal(0, blocked);
        Assert.Equal(index, CoreTutorial.CurrentStepIndex);
        Click(host, target);
        Assert.Equal(1, clicked);
        Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
    });

    [Fact]
    public Task ClosingLiveCardRestoresHostInputAndLeavesNoCardOrSubscriptions() => WithHost((host, overlay, target, _, _) =>
    {
        overlay.Close();
        Assert.False(CoreTutorial.IsActive);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
        var clicked = 0;
        target.Click += (_, _) => clicked++;
        Click(host, target);
        Assert.Equal(1, clicked);
    });
    [Fact]
    public Task HostCloseEndsTheTourAndRaisesClosedOnce() => WithHost((host, overlay, _, _, _) =>
    {
        var closed = 0;
        overlay.Closed += (_, _) => closed++;
        host.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreTutorial.IsActive);
        Assert.False(overlay.IsOnStage);
        overlay.Close();
        Assert.Equal(1, closed);
    });

    [Fact]
    public Task HostCloseKeepsTheTourWhenTheStepNamesItsOwnWindow() => WithHost((host, overlay, _, _, index) =>
    {
        // WPF TutorialOverlay.xaml.cs:264-268: any named target window defers to WindowLoaded, even the closing one.
        var provider = CoreTutorial.CurrentStepProvider;
        try
        {
            CoreTutorial.CurrentStepProvider = () => provider?.Invoke() is { } s
                ? new CoreTutorial.Step { Id = s.Id, Title = s.Title, Description = s.Description, Icon = s.Icon,
                    TargetElementName = s.TargetElementName, TargetWindowTypeName = host.GetType().Name,
                    TextPosition = s.TextPosition, Advance = s.Advance, AllowManualSkip = s.AllowManualSkip,
                    BlockBackgroundClicks = s.BlockBackgroundClicks }
                : null;
            host.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
        }
        finally { CoreTutorial.CurrentStepProvider = provider; }
    });

    [Fact]
    public Task HostEscapeEndsTheTourWithoutTakingFocus() => WithHost((host, overlay, target, _, _) =>
    {
        target.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(target, host.FocusManager?.GetFocusedElement()); // the card did not pull focus back
        host.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreTutorial.IsActive);
        PumpUntil(() => !overlay.IsOnStage);
        Assert.Same(target, host.FocusManager?.GetFocusedElement()); // Escape reached the host while target kept focus
    });

    private static void PumpUntil(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();   // P73: bounded by time, not turns
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(10)) Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }

    private static global::Avalonia.Animation.DoubleTransition OpacityFade(Control stage) =>
        Assert.Single(stage.Transitions!.OfType<global::Avalonia.Animation.DoubleTransition>(), t => t.Property == Visual.OpacityProperty);

    [Fact]
    public Task CardFadesIn300AndOut200BeforeLeavingTheHost() => WithHost((host, overlay, _, _, _) =>
    {
        // WPF TutorialOverlay.xaml.cs:107 / :1308.
        var stage = overlay.StageRoot!;
        Assert.Equal(TimeSpan.FromMilliseconds(300), OpacityFade(stage).Duration);
        CoreTutorial.Skip();
        Dispatcher.UIThread.RunJobs();
        Assert.True(overlay.IsOnStage);                  // still fading out
        Assert.Equal(TimeSpan.FromMilliseconds(200), OpacityFade(stage).Duration);
        var closed = 0;
        overlay.Closed += (_, _) => closed++;
        PumpUntil(() => !overlay.IsOnStage);
        Assert.Equal(1, closed);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
    });

    [Fact]
    public Task PanicTearsDownWithoutTheFade() => WithHost((_, overlay, _, _, _) =>
    {
        TutorialHead.OnPanic();
        Assert.False(CoreTutorial.IsActive);
        Assert.False(overlay.IsOnStage);
    });

    [Fact]
    public Task StepForAWindowNotYetOpenShowsTheCentredWaitingCard() => WithHost((host, overlay, target, _, index) =>
    {
        // WPF UpdateStep :466-486: only Skip, full dim, no spotlight measured on the old window.
        var core = CoreTutorial.CurrentStep!;
        var provider = CoreTutorial.CurrentStepProvider;
        var windows = TutorialOverlay.Windows;
        try
        {
            var waiting = new CoreTutorial.Step
            {
                Id = core.Id, Title = "Waiting", Description = core.Description, Icon = core.Icon,
                TargetElementName = core.TargetElementName, TargetWindowTypeName = "NotOpenWindow",
                TextPosition = core.TextPosition, Advance = core.Advance, AllowManualSkip = true,
                BlockBackgroundClicks = true,
            };
            TutorialOverlay.Windows = () => new[] { host };
            CoreTutorial.CurrentStepProvider = () => waiting;
            CoreTutorial.RaiseStepChanged(null, waiting);
            Dispatcher.UIThread.RunJobs();
            var root = overlay.StageRoot!;
            bool Shown(string name) => root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).IsVisible;
            Assert.True(Shown("BtnSkip"));
            Assert.False(Shown("BtnSkipStep"));
            Assert.False(Shown("BtnNext"));
            Assert.False(Shown("BtnPrevious"));
            Assert.Equal("Waiting", root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TxtTitle").Text);
            var canvas = root.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "SpotlightCanvas");
            Assert.IsType<global::Avalonia.Controls.Shapes.Rectangle>(Assert.Single(canvas.Children)); // full dim, no hole or glow ring
            var clicked = 0;
            target.Click += (_, _) => clicked++;
            Click(host, target);                          // the old window's control is not a hole
            Assert.Equal(0, clicked);
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
        }
        finally
        {
            CoreTutorial.CurrentStepProvider = provider;
            TutorialOverlay.Windows = windows;
        }
    });

    [Fact]
    public Task StepBehindASectionLightsItsRailRowWithoutNavigating() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldStore = TutorialService.CompletionStore;
        var oldShell = TutorialHeadHooks.ShellProvider;
        TutorialService.CompletionStore = new Store();
        TutorialHeadHooks.ShellProvider = () => null;     // no tab navigation: only the overlay can light a row
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var shell = new MainShellWindow();
        TutorialOverlay? overlay = null;
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            string[] Lit() => shell.NavSectionRowsForTests.Where(r => r.Button.Classes.Contains("active")).Select(r => r.Section).ToArray();
            var before = Lit();
            TutorialHead.Service.Start(TutorialType.FullTour);
            string? want = null;
            while (TutorialHead.Service.IsActive)
            {
                var key = TutorialService.DoorTabKeyFor(TutorialHead.Service.CurrentStep!);
                want = key == null ? null : ConditioningControlPanel.Services.UI.NavSections.SectionForTab(key);
                if (want != null && !before.Contains(want)) break;
                TutorialHead.Service.Next();
            }
            Assert.NotNull(want);
            overlay = new TutorialOverlay(shell);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { want }, Lit());
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            shell.Close();
            TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeInsideAHostTextBoxStaysWithTheTextBox() => WithHost((host, overlay, _, _, index) =>
    {
        var box = new TextBox { Width = 120 };
        ((Canvas)host.Content!).Children.Add(box);
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        host.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreTutorial.IsActive);
        Assert.True(overlay.IsOnStage);
        Assert.Equal(index, CoreTutorial.CurrentStepIndex);
    });

    private sealed class SecondHost : Window { }

    [Fact]
    public Task WindowLoadedMovesCardAndTriggerToTheNewHost() => WithHost((host, overlay, target, _, index) =>
    {
        var windows = TutorialOverlay.Windows;
        var core = CoreTutorial.CurrentStep!;
        var provider = CoreTutorial.CurrentStepProvider;
        var second = new SecondHost { Width = 900, Height = 650 };
        try
        {
            var otherTarget = new Button { Name = target.Name, Width = 160, Height = 42 };
            Canvas.SetLeft(otherTarget, 40); Canvas.SetTop(otherTarget, 60);
            second.Content = new Canvas { Children = { otherTarget } };
            second.Show();
            TutorialOverlay.Windows = () => new[] { host, second };
            var redirected = new CoreTutorial.Step
            {
                Id = core.Id, Title = core.Title, Description = core.Description, Icon = core.Icon,
                TargetElementName = core.TargetElementName, TargetWindowTypeName = nameof(SecondHost),
                TextPosition = core.TextPosition, Advance = core.Advance, AllowManualSkip = core.AllowManualSkip,
                BlockBackgroundClicks = core.BlockBackgroundClicks,
            };
            CoreTutorial.CurrentStepProvider = () => provider?.Invoke() is { } current && current.Id == core.Id
                ? redirected : provider?.Invoke();
            CoreTutorialEvents.Emit("WindowLoaded:" + nameof(SecondHost));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, overlay.StageWindow);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
            Assert.Contains(second.GetVisualDescendants().OfType<Border>(), b => b.Name == "TextPanel");
            Click(host, target); // old trigger unsubscribed
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            Click(second, otherTarget);
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        }
        finally
        {
            CoreTutorial.CurrentStepProvider = provider;
            TutorialOverlay.Windows = windows;
            second.Close();
        }
    });

    [Fact]
    public Task ResizeUpdatesStageAndRunsTargetPreparation() => WithHost((host, overlay, _, _, _) =>
    {
        var step = TutorialHead.CurrentHeadStep!;
        var prepare = step.PrepareTargetWindowAction;
        var count = 0;
        try
        {
            step.PrepareTargetWindowAction = window => { Assert.Same(host, window); count++; };
            host.Width = 1000;
            host.Height = 700;
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1000, overlay.StageRoot!.Bounds.Width);
            Assert.Equal(700, overlay.StageRoot.Bounds.Height);
            Assert.True(count > 0);
        }
        finally { step.PrepareTargetWindowAction = prepare; }
    });

}
