using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialLiveTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }
    private sealed class OtherWindow : Window { }

    internal static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Task WithOverlay(Action<TutorialStep, StackPanel> configure, Action<Window, TutorialOverlay, StackPanel> check)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var store = TutorialService.CompletionStore;
        var shell = TutorialHeadHooks.ShellProvider;
        var lookup = TutorialOverlay.FindTargetWindow;
        var owner = new Window();
        var host = new Window();
        TutorialOverlay? overlay = null;
        try
        {
            TutorialService.CompletionStore = new Store();
            TutorialHeadHooks.ShellProvider = () => null;
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHead.Service.ConfigureCallbacks(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }, showTab: _ => { });
            TutorialHead.Service.Start(TutorialType.Modding);
            // Configure a card before the service first projects it; Next still uses the real engine.
            var step = TutorialHead.Service.CurrentSteps[1];
            step.Id = "live-test";
            step.TargetWindowTypeName = null;
            step.TargetElementName = "Target";
            step.TextPosition = TutorialStepPosition.Bottom;
            step.AdvanceTrigger = TutorialAdvanceTrigger.Manual;
            step.PrepareTargetWindowAction = null;
            step.OnActivate = null;
            var body = new StackPanel();
            configure(step, body);
            host.Content = body;
            owner.Show();
            host.Show(owner);
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Service.Next();
            overlay = new TutorialOverlay(host);
            overlay.Show(owner);
            Dispatcher.UIThread.RunJobs();
            check(host, overlay, body);
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            host.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            TutorialOverlay.FindTargetWindow = lookup;
            TutorialHeadHooks.ShellProvider = shell;
            TutorialService.CompletionStore = store;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EventAdvanceIsExactOnceAndOldQueuedAdvanceCannotSkipANewCard() => WithOverlay((s, _) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnEvent;
        s.AdvanceEventName = "FileSaved";
    }, (_, overlay, _) =>
    {
        CoreTutorialEvents.Emit("Other");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        Assert.False(overlay.Topmost);
        Assert.False(overlay.ShowActivated);
        Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(overlay.Transitions!)).Duration);
        CoreTutorialEvents.Emit("FileSaved");
        CoreTutorialEvents.Emit("FileSaved");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
        overlay.Close();
        TutorialHead.Service.Start(TutorialType.Modding);
        CoreTutorialEvents.Emit("FileSaved");
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreTutorial.IsActive);
        Assert.Equal(0, CoreTutorial.CurrentStepIndex);
    });

    [Fact]
    public Task TextRequiresAnEditEvenWhenItAlreadyMatchesAndPrepRunsBeforeLookup() => WithOverlay((s, body) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals;
        s.AdvanceValue = "Pulse";
        s.PrepareTargetWindowAction = _ =>
        {
            if (body.Children.Count == 0) body.Children.Add(new TextBox { Name = "Target", Text = "Pulse" });
        };
    }, (_, _, body) =>
    {
        var text = Assert.IsType<TextBox>(Assert.Single(body.Children));
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        text.Text = "Other";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        text.Text = "Pulse curve";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
    });

    [Theory]
    [InlineData(true, "tag")]
    [InlineData(false, "label")]
    [InlineData(false, "")]
    public Task SelectionMatchesTagContentOrAnyChange(bool byTag, string expected) => WithOverlay((s, body) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals;
        s.AdvanceValue = expected;
        s.MatchByTag = byTag;
        body.Children.Add(new ComboBox { Name = "Target", ItemsSource = new[]
        {
            new ComboBoxItem { Content = "wrong", Tag = "wrong" },
            new ComboBoxItem { Content = "LABEL", Tag = "TAG" }
        }, SelectedIndex = 0 });
    }, (_, _, body) =>
    {
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        ((ComboBox)body.Children[0]).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
    });

    private static PointerReleasedEventArgs Release(MouseButton button = MouseButton.Left) => new(InputElement.PointerReleasedEvent, new Pointer(1, PointerType.Mouse, true),
        null, default, 0, default, KeyModifiers.None, button);

    [Theory]
    [InlineData(9, 10, 20, false)]
    [InlineData(10, 10, 20, true)]
    [InlineData(20, 10, 20, true)]
    [InlineData(21, 10, 20, false)]
    [InlineData(50, 10, double.NaN, true)]
    public Task SliderWaitsForReleaseAndUsesInclusiveRange(double value, double min, double max, bool advance) => WithOverlay((s, body) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnSliderAtLeast;
        s.AdvanceMinValue = min;
        s.AdvanceMaxValue = max;
        body.Children.Add(new Slider { Name = "Target", Maximum = 100 });
    }, (_, _, body) =>
    {
        var slider = (Slider)body.Children[0];
        slider.Value = value;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        slider.RaiseEvent(Release(MouseButton.Right));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, CoreTutorial.CurrentStepIndex);
        slider.RaiseEvent(Release());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(advance ? 2 : 1, CoreTutorial.CurrentStepIndex);
    });

    [Fact]
    public Task ButtonReleaseThenClickAdvancesOnceWithoutInterruptingItsClick() => WithOverlay((s, body) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick;
        body.Children.Add(new Button { Name = "Target" });
    }, (_, _, body) =>
    {
        var button = (Button)body.Children[0];
        bool clicked = false;
        button.Click += (_, _) => { clicked = true; Assert.Equal(1, CoreTutorial.CurrentStepIndex); };
        button.RaiseEvent(Release());
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(clicked);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
    });

    [Fact]
    public Task PendingButtonAdvanceDoesNotWalkAnExplicitlySelectedSuccessor() => WithOverlay((s, body) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick;
        body.Children.Add(new Button { Name = "Target" });
    }, (_, _, body) =>
    {
        ((Button)body.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CoreTutorial.Next(); // another route moved before the deferred button callback
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
    });

    [Fact]
    public Task MissingWindowShowsWaitingCardThenWindowLoadedRetargetsWithoutAdvancing() => WithOverlay((s, _) =>
    {
        s.TargetWindowTypeName = nameof(OtherWindow);
    }, (_, overlay, _) =>
    {
        Assert.False(overlay.FindControl<Button>("BtnNext")!.IsVisible);
        var other = new OtherWindow();
        try
        {
            other.Show();
            TutorialOverlay.FindTargetWindow = type => type == nameof(OtherWindow) ? other : null;
            CoreTutorialEvents.Emit("WindowLoaded:" + nameof(OtherWindow));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, CoreTutorial.CurrentStepIndex);
            Assert.Same(other, overlay.TargetWindow);
            Assert.True(overlay.FindControl<Button>("BtnNext")!.IsVisible);
        }
        finally { other.Close(); }
    });

    [Fact]
    public Task WindowLoadFallbackRetargetsAndOldHostCloseDoesNotEndTheTour() => WithOverlay((_, _) => { }, (host, overlay, _) =>
    {
        var next = TutorialHead.Service.CurrentSteps[2];
        next.TargetWindowTypeName = nameof(OtherWindow);
        next.AdvanceTrigger = TutorialAdvanceTrigger.Manual;
        var other = new OtherWindow();
        try
        {
        other.Show();
        TutorialOverlay.FindTargetWindow = type => type == nameof(OtherWindow) ? other : null;
        CoreTutorialEvents.Emit("WindowLoaded:" + nameof(OtherWindow));
        host.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreTutorial.IsActive);
        Assert.Equal(2, CoreTutorial.CurrentStepIndex);
        Assert.Same(other, overlay.TargetWindow);
        other.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreTutorial.IsActive);
        Assert.Equal(0, overlay.Opacity);
        }
        finally { other.Close(); }
    });

    [Fact]
    public Task InteractiveDialogQueuesPartTwoOnlyAfterValidCreate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var owner = new Window();
        var dialog = new ConditioningControlPanel.Avalonia.Views.Deeper.NewEnhancementDialog();
        var pending = CoreTutorialEvents.PendingPart2Tutorial;
        try
        {
            CoreTutorialEvents.PendingPart2Tutorial = null;
            owner.Show();
            var result = dialog.ShowDialog<bool>(owner);
            dialog.FindControl<Button>("BtnTryHypnoTubeTutorial")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("DeeperEditorInteractiveHT", CoreTutorial.CurrentTourName);
            Assert.True(CoreTutorial.IsActive);
            dialog.FindControl<TextBox>("TxtSource")!.Text = "";
            dialog.FindControl<Button>("BtnCreate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
            Assert.False(result.IsCompleted);
            dialog.FindControl<TextBox>("TxtSource")!.Text = "https://example.com/tutorial";
            dialog.FindControl<Button>("BtnCreate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("DeeperEditorInteractiveHTPart2", CoreTutorialEvents.PendingPart2Tutorial);
            Assert.True(result.IsCompletedSuccessfully);
        }
        finally
        {
            dialog.Close();
            TutorialHead.ResetForTests();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            CoreTutorialEvents.PendingPart2Tutorial = pending;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicClosesLiveSubscriptionsAndAbandonsWithoutCompletion() => WithOverlay((s, _) =>
    {
        s.AdvanceTrigger = TutorialAdvanceTrigger.OnEvent;
        s.AdvanceEventName = "FileSaved";
    }, (_, overlay, _) =>
    {
        PanicSurfaces.All.First(s => s.Id == "tour").Stop(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreTutorial.IsActive);
        Assert.False(overlay.IsVisible);
        CoreTutorialEvents.Emit("FileSaved");
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreTutorial.IsActive);
    });
}
