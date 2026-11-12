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

    private static Task WithHost(Action<Window, TutorialOverlay, Button, Button, int> check) =>
        WithHost((host, overlay, target, outside, index) =>
        {
            check(host, overlay, target, outside, index);
            return Task.CompletedTask;
        });

    private static Task WithHost(Func<Window, TutorialOverlay, Button, Button, int, Task> check) => AvaloniaTestDispatcher.RunAsync(async () =>
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
            await check(host, overlay, target, outside, CoreTutorial.CurrentStepIndex);
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
    public Task LiveCardFadesInOver300Milliseconds() => WithHost((_, overlay, _, _, _) =>
    {
        var root = Assert.IsAssignableFrom<Control>(overlay.StageRoot);
        // The headless render timer drives Avalonia's real opacity transition.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        System.Threading.Thread.Sleep(60);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(root.Opacity, 0.01, 0.95);
        System.Threading.Thread.Sleep(400);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, root.Opacity, 2);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FinishAndSkipFadeBeforeDetach(bool finish) => WithHost(async (host, overlay, _, _, _) =>
    {
        var root = overlay.StageRoot!;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        System.Threading.Thread.Sleep(400);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        if (finish) while (CoreTutorial.IsActive) CoreTutorial.Next();
        else CoreTutorial.Skip();
        Assert.True(overlay.IsOnStage);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        System.Threading.Thread.Sleep(80);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(root.Opacity, 0.01, 0.99);
        await Task.Delay(250);
        Dispatcher.UIThread.RunJobs();
        Assert.False(overlay.IsOnStage);
    });

    [Fact]
    public Task FadingStageKeepsItsInputRegionAndIgnoresOldCardActions() => WithHost(async (host, overlay, target, outside, _) =>
    {
        var outsideClicks = 0;
        var targetClicks = 0;
        outside.Click += (_, _) => outsideClicks++;
        target.Click += (_, _) => targetClicks++;
        var oldSkip = overlay.StageRoot!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BtnSkip");
        CoreTutorial.Skip();
        Assert.True(overlay.IsOnStage);

        var outsidePoint = outside.TranslatePoint(new Point(outside.Bounds.Width / 2, outside.Bounds.Height / 2), host)!.Value;
        var targetPoint = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host)!.Value;
        host.MouseDown(outsidePoint, MouseButton.Left);
        host.MouseUp(outsidePoint, MouseButton.Left);
        host.MouseDown(targetPoint, MouseButton.Left);
        host.MouseUp(targetPoint, MouseButton.Left);
        Assert.Equal(0, outsideClicks);
        Assert.Equal(1, targetClicks);

        TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
        oldSkip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(CoreTutorial.IsActive);
        await Task.Delay(250);
        Assert.False(overlay.IsOnStage);
        Click(host, outside);
        Assert.Equal(1, outsideClicks);
    });

    [Fact]
    public Task PanicDetachesImmediately() => WithHost((_, overlay, _, _, _) =>
    {
        TutorialHead.OnPanic();
        Assert.False(overlay.IsOnStage);
        Assert.False(CoreTutorial.IsActive);
    });

    [Fact]
    public Task HostCloseDuringFadeDetachesImmediately() => WithHost((host, overlay, _, _, _) =>
    {
        CoreTutorial.Skip();
        Assert.True(overlay.IsOnStage);
        host.Close();
        Assert.False(overlay.IsOnStage);
    });

    [Fact]
    public Task RestartDuringFadeKeepsNewStage() => WithHost(async (host, overlay, _, _, _) =>
    {
        CoreTutorial.Skip();
        Assert.True(overlay.IsOnStage);
        TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
        var successor = new TutorialOverlay(host);
        try
        {
            successor.Show();
            Assert.NotSame(overlay.StageRoot, successor.StageRoot);
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            Assert.False(overlay.IsOnStage);
            Assert.True(successor.IsOnStage);
        }
        finally { successor.Close(); }
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
        Assert.False(overlay.IsOnStage);
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
    public Task HostEscapeEndsTheTourWithoutTakingFocus() => WithHost(async (host, overlay, target, _, _) =>
    {
        target.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(target, host.FocusManager?.GetFocusedElement()); // the card did not pull focus back
        host.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Assert.False(CoreTutorial.IsActive);
        Assert.True(overlay.IsOnStage);
        Assert.Same(target, host.FocusManager?.GetFocusedElement()); // Escape reached the host while target kept focus
        await Task.Delay(700); // dispatcher may be delayed beyond the fade duration
        Assert.False(overlay.IsOnStage);
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
