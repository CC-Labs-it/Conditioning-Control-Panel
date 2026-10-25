using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialAdvanceTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    private static Task WithCard(string id, Control target, Action<TutorialOverlay, Window, int> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldStore = TutorialService.CompletionStore;
            var oldShell = TutorialHeadHooks.ShellProvider;
            TutorialService.CompletionStore = new Store();
            TutorialHeadHooks.ShellProvider = () => null;
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            var host = new Window { Width = 900, Height = 650 };
            TutorialOverlay? overlay = null;
            try
            {
                TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
                while (TutorialHead.Service.IsActive && TutorialHead.Service.CurrentStep!.Id != id) TutorialHead.Service.Next();
                Assert.True(TutorialHead.Service.IsActive, "Card absent from test tour: " + id);
                target.Name = TutorialHead.Service.CurrentStep.TargetElementName;
                // Leave room for the in-window card beside the actual control. A full-window
                // slider makes the card overlap the fixture's pointer position by construction.
                target.Width = 300;
                target.Height = 42;
                Canvas.SetLeft(target, 40);
                Canvas.SetTop(target, 80);
                host.Content = new Canvas { Children = { target } };
                host.Show();
                overlay = new TutorialOverlay(host);
                overlay.Show();
                Dispatcher.UIThread.RunJobs();
                check(overlay, host, CoreTutorial.CurrentStepIndex);
            }
            finally
            {
                overlay?.Close();
                TutorialHead.ResetForTests();
                host.Close();
                Dispatcher.UIThread.RunJobs();
                TutorialService.CompletionStore = oldStore;
                TutorialHeadHooks.ShellProvider = oldShell;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task TextRequiresAnEditAndMatchingValueAndDetachesAfterAdvance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var text = new TextBox { Text = "15" };
        return WithCard("iht_ruletime", text, (_, _, index) =>
        {
            Assert.Equal(index, CoreTutorial.CurrentStepIndex); // matching prefilled text is no edit
            text.Text = "115";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            text.Text = "15.1";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
            text.Text = "15";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task SelectionMatchesTagInsteadOfLocalizedContent() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var combo = new ComboBox
        {
            ItemsSource = new[]
            {
                new ComboBoxItem { Content = "screen_shake", Tag = "wrong" },
                new ComboBoxItem { Content = "Localized label", Tag = "SCREEN_SHAKE" },
            },
        };
        return WithCard("iht_ruleaction", combo, (_, _, index) =>
        {
            combo.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            combo.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task ClickIsDeferredAndOnlyAdvancesOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var button = new Button();
        return WithCard("iht_play", button, (_, _, index) =>
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task PointerReleaseAdvancesEvenWhenTheButtonNeverRaisesClick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        return WithCard("iht_play", button, (_, host, index) =>
        {
            // A press swallowed before the button means no Click; only the tunnel release handler
            // (WPF PreviewMouseLeftButtonUp) can advance, as when a dialog closes on its own click.
            host.AddHandler(InputElement.PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
            var clicked = false;
            button.Click += (_, _) => clicked = true;
            host.MouseDown(new Point(100, 100), MouseButton.Left);
            host.MouseUp(new Point(100, 100), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(clicked);
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task ClosingBeforeDeferredClickCannotAdvanceSuccessorTour() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var button = new Button();
        return WithCard("iht_play", button, (overlay, _, index) =>
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.Close();
            TutorialHead.Service.Start(TutorialType.ShortWalk);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task EmptySelectionExpectationAcceptsAnyChangedSelection() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "existing", "chosen" }, SelectedIndex = 0 };
        return WithCard("iht_pattern", combo, (_, _, index) =>
        {
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            combo.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });

    [Fact]
    public Task EventMustMatchAndCloseCancelsQueuedAdvance() => AvaloniaTestDispatcher.RunAsync(() =>
        WithCard("iht_addeffect", new Button(), (overlay, _, index) =>
        {
            CoreTutorialEvents.Emit("effectadded");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            CoreTutorialEvents.Emit("EffectAdded");
            CoreTutorialEvents.Emit("EffectAdded");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
            overlay.Close();
            TutorialHead.Service.Start(TutorialType.ShortWalk);
            CoreTutorialEvents.Emit("EffectAdded");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);
        }));

    [Fact]
    public Task SliderAdvancesOnlyOnReleaseInsideRequestedRange() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.1 };
        return WithCard("iht_intensity", slider, (_, host, index) =>
        {
            slider.Value = 0.5;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            host.MouseDown(new Point(100, 100), MouseButton.Left);
            slider.Value = 0.9;
            host.MouseUp(new Point(100, 100), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, CoreTutorial.CurrentStepIndex);
            host.MouseDown(new Point(100, 100), MouseButton.Left);
            slider.Value = 0.5;
            host.MouseUp(new Point(100, 100), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index + 1, CoreTutorial.CurrentStepIndex);
        });
    });
}
