using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChatThresholdMotionTests
{
    private static void Step(SteppedTime time, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        BeatLoop.StepAll();
    }

    [Fact]
    public Task ThinkingDotsStaggerParkResetAndDetach() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previousTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var view = new ChatThresholdView();
        var ancestor = new Border { Child = view };
        var window = new Window { Content = ancestor, Width = 700, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var vm = view.ViewModel!;
            var dots = Enumerable.Range(1, 3).Select(i => view.FindControl<Ellipse>("Dot" + i)!).ToArray();
            var owner = view.FindControl<StackPanel>("ThinkingDots")!;
            vm.IsThinking = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 0.25, 1.0, 1.0 }, dots.Select(d => d.Opacity));
            var loop = Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, owner));
            Assert.True(loop.OnFrameBeat);

            // WPF clones a 450ms AutoReverse animation, with BeginTime 0/150/300ms.
            Step(time, 0.15);
            Assert.Equal(0.5, dots[0].Opacity, 3);
            Assert.Equal(0.25, dots[1].Opacity, 3);
            Assert.Equal(1, dots[2].Opacity, 3);
            Step(time, 0.3);
            Assert.Equal(1, dots[0].Opacity, 3);
            Assert.Equal(0.75, dots[1].Opacity, 3);
            Assert.Equal(0.5, dots[2].Opacity, 3);
            Step(time, 0.6); // each storyboard repeats its own delay too
            Assert.Equal(1, dots[1].Opacity, 3);
            Step(time, 0.15);
            Assert.Equal(1, dots[2].Opacity, 3);
            Assert.All(dots, d => Assert.InRange(d.Opacity, 0, 1));

            ancestor.IsVisible = false;
            Assert.False(loop.IsTicking);
            ancestor.IsVisible = true;
            Assert.True(loop.IsTicking);
            window.WindowState = WindowState.Minimized;
            Assert.False(loop.IsTicking);
            window.WindowState = WindowState.Normal;
            Assert.True(loop.IsTicking);

            vm.IsThinking = false;
            Assert.False(loop.IsRunning);
            Assert.All(dots, d => Assert.Equal(1, d.Opacity));
            vm.IsThinking = true;
            Assert.True(loop.IsRunning);
            ancestor.Child = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(loop.IsRunning);
            vm.IsThinking = false;
            vm.IsThinking = true; // a detached view must not restart from its old VM
            Assert.False(loop.IsRunning);
            ancestor.Child = view;
            Dispatcher.UIThread.RunJobs();
            Assert.True(loop.IsRunning);
            return Task.CompletedTask;
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = previousTime;
        }
    });

    [Fact]
    public Task DormantStateChangeStartsOneDelayedShimmer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previousTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var view = new ChatThresholdView();
        var window = new Window { Content = view, Width = 700, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var vm = view.ViewModel!;
            var shimmer = view.FindControl<Border>("DormantShimmer")!;
            var host = view.FindControl<Border>("DormantHost")!;
            var shift = ((TransformGroup)shimmer.RenderTransform!).Children.OfType<TranslateTransform>().Single();
            Assert.Equal(0, shimmer.Opacity);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), shimmer.RenderTransformOrigin);
            void State(CompanionZoneState state) => typeof(ChatThresholdViewModel)
                .GetProperty(nameof(ChatThresholdViewModel.State))!.SetValue(vm, state);
            // Sample before the state transition: a collapsed host can legitimately use WPF's fallback.
            var travel = host.Bounds.Width > 1 ? host.Bounds.Width + 90 : 480;
            State(CompanionZoneState.Dormant);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shimmer.Opacity);
            Assert.Equal(-90, shift.X);
            var loop = Assert.Single(BeatLoop.Running, l => ReferenceEquals(l.Owner, shimmer));
            Step(time, 0.25);
            Assert.Equal(-90, shift.X); // WPF BeginTime, before the 1.4s CubicEaseInOut sweep
            Step(time, 0.7);
            Assert.Equal((-90 + travel) / 2, shift.X, 3);
            Step(time, 0.7);
            Assert.Equal(travel, shift.X, 3);
            Assert.False(loop.IsRunning);
            State(CompanionZoneState.Live);
            State(CompanionZoneState.Dormant);
            Assert.False(loop.IsRunning);
            Assert.Equal(travel, shift.X, 3);
            return Task.CompletedTask;
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = previousTime;
        }
    });
}
