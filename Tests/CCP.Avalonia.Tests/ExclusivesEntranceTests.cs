using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesEntranceTests
{
    [Fact]
    public Task PremiumStaggersEveryShowAndPlansNeverDoes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var clock = new Clock();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        BeatLoop.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = (MotionLevel.Full, false, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("premium"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            Border[] Items() => view.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "ExCard" || b.Name == "GroupHead").ToArray();
            var items = Items();
            var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
            Assert.Single(BeatLoop.Running.Where(loop => loop.Owner == shelf));
            Assert.True(items.Length > 2);
            var card = items.First(b => b.Name == "ExCard");
            int index = Array.IndexOf(items, card);
            clock.Now = TimeSpan.FromMilliseconds(index * 38 - 1).Ticks; BeatLoop.StepAll();
            Assert.Equal(0, card.Opacity);
            var group = Assert.IsType<TransformGroup>(card.RenderTransform);
            var scale = group.Children.OfType<ScaleTransform>().Single();
            var slide = group.Children.OfType<TranslateTransform>().Single();
            Assert.Equal(18, slide.Y, 6);
            clock.Now = TimeSpan.FromMilliseconds(index * 38 + 420).Ticks; BeatLoop.StepAll();
            Assert.Equal(1, card.Opacity, 6);
            Assert.Equal(0, slide.Y, 6);
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            Assert.Equal(1.02, scale.GetBaseValue(ScaleTransform.ScaleXProperty));
            shell.ShowTab("settings"); Settle();
            settings.MotionLevel = MotionLevel.Reduced;
            shell.ShowTab("premium"); Settle();
            items = Items(); card = items.First(b => b.Name == "ExCard"); index = Array.IndexOf(items, card);
            slide = Assert.IsType<TransformGroup>(card.RenderTransform).Children.OfType<TranslateTransform>().Single();
            clock.Now += TimeSpan.FromMilliseconds(index * 20 - 1).Ticks; BeatLoop.StepAll();
            Assert.Equal(0, card.Opacity); Assert.Equal(0, slide.Y);
            clock.Now += TimeSpan.FromMilliseconds(201).Ticks; BeatLoop.StepAll();
            Assert.Equal(1, card.Opacity, 6);
            shell.ShowTab("settings"); Settle();
            settings.MotionLevel = MotionLevel.Off;
            shell.ShowTab("premium"); Settle();
            Assert.All(Items(), item => Assert.Equal(1, item.Opacity));
            var plans = new ExclusivesTabView { PlansMode = true };
            plans.PlayVaultEntrance();
            Assert.Empty(plans.GetVisualDescendants().OfType<Border>().Where(b => b.Opacity == 0));
        }
        finally
        {
            shell?.Close(); BeatLoop.Time = TimeProvider.System;
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public Task MinimizeCompletesEntranceBeforeRestore(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var clock = new Clock();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        BeatLoop.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = (level, false, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("premium"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var items = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Name == "ExCard" || b.Name == "GroupHead").ToArray();
            Assert.True(items.Length > 2);
            Assert.Contains(items, item => item.Opacity == 0);
            shell.WindowState = WindowState.Minimized; Settle();
            shell.WindowState = WindowState.Normal; Settle();
            Assert.All(items, item =>
            {
                Assert.Equal(1, item.Opacity, 6);
                var slide = item.RenderTransform is TransformGroup group
                    ? group.Children.OfType<TranslateTransform>().Single()
                    : Assert.IsType<TranslateTransform>(item.RenderTransform);
                Assert.Equal(0, slide.Y, 6);
            });
        }
        finally
        {
            shell?.Close(); BeatLoop.Time = TimeProvider.System;
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
