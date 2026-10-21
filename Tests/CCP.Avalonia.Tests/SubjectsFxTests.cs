using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SubjectsFxTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task CardsStaggerOnlyOnArrivalWithoutUndimmingTakenSubjects() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new AvailableSubjectsTabView();
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            list.ItemsSource = Enumerable.Range(0, 9).Select(i => new SubjectRow { Claimed = i == 2 }).ToArray();
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.IsVisible = false;
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var cards = Enumerable.Range(0, 9).Select(i => list.ContainerFromIndex(i)!).ToArray();
            Assert.Equal(0, cards[0].Opacity);
            Assert.Equal(10, Assert.IsType<TranslateTransform>(cards[0].RenderTransform).Y);
            Step(view, clock, 100);
            Assert.InRange(cards[0].Opacity, 0.1, 0.99);
            Assert.True(cards[0].Opacity > cards[2].Opacity);
            Step(view, clock, 160);
            Assert.True(cards[5].Opacity > cards[6].Opacity);
            Assert.Equal(cards[6].Opacity, cards[8].Opacity);
            Step(view, clock, 300);
            Assert.All(cards, c => Assert.Equal(1, c.Opacity));
            Assert.False(view.SubjectsFxRunning);
            Assert.Equal(0.6, cards[2].GetVisualDescendants().OfType<Border>().First(b => b.Width == 260).Opacity);

            // A service rebind while the page is being read must never replay the entrance.
            list.ItemsSource = new[] { new SubjectRow() };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            view.IsVisible = false; view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity);
            view.IsVisible = false;
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            Assert.False(view.SubjectsFxRunning);
        }
        finally
        {
            window?.Close();
            SetClock(TimeProvider.System);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ConnectSquishesOnPressAndRestoresOnReleaseLeaveAndHide() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        SetClock(clock);
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            var view = new AvailableSubjectsTabView();
            window = new Window { Width = 1300, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            var button = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity); // Reduced still permits interaction entrances
            Step(view, clock, 600);
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            Step(view, clock, 40);
            var scale = Assert.IsType<ScaleTransform>(button.RenderTransform);
            Assert.InRange(scale.ScaleX, 0.97, 0.999);
            Step(view, clock, 40);
            Assert.Equal(0.97, scale.ScaleX, 6);
            window.MouseUp(centre, MouseButton.Left);
            Step(view, clock, 80);
            Assert.Equal(1, scale.ScaleX, 6);
            window.MouseDown(centre, MouseButton.Left);
            Step(view, clock, 80);
            window.MouseMove(new Point(1290, 690));
            Step(view, clock, 80);
            Assert.Equal(1, scale.ScaleX, 6);
            window.MouseUp(new Point(1290, 690), MouseButton.Left);

            // Off snaps the same states, while an entrance never starts.
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            view.IsVisible = false; view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            Assert.Equal(0.97, scale.ScaleX, 6);
            view.IsVisible = false;
            Assert.Equal(1, scale.ScaleX, 6);
            window.MouseUp(centre, MouseButton.Left);
        }
        finally
        {
            window?.Close();
            SetClock(TimeProvider.System);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SocialDoorStartsEntranceAndLeavingParksIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var motion = CoreSettings.Current.MotionLevel;
        var clock = new Clock();
        SetClock(clock);
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Full, CoreSettings.Current.MotionLevel);
            shell.OpenNavSection("social");
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
            var list = view.FindControl<ItemsControl>("AvailableSubjectsList")!;
            Assert.True(view.IsEffectivelyVisible);
            Assert.Equal(0, list.ContainerFromIndex(0)!.Opacity);
            Assert.True(view.SubjectsFxRunning);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.SubjectsFxRunning);
            Assert.Equal(1, list.ContainerFromIndex(0)!.Opacity);
        }
        finally
        {
            shell?.Close();
            CoreSettings.Current.MotionLevel = motion;
            SetClock(TimeProvider.System);
        }
        return Task.CompletedTask;
    });

    private static void SetClock(TimeProvider clock) => AvailableSubjectsTabView.FxTime = clock;
    private static void Step(AvailableSubjectsTabView view, Clock clock, int ms)
    {
        clock.Now += TimeSpan.FromMilliseconds(ms).Ticks;
        view.StepSubjectsFx();
        Dispatcher.UIThread.RunJobs();
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
