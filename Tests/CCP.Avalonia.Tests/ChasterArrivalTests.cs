using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChasterArrivalTests
{
    [Fact]
    public void VisibleCardsStaggerOnEveryVisitAndOffSnaps() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreSettings.Current.MotionLevel;
        var clock = new Clock();
        ChasterTabView.ArrivalTime = clock;
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            var view = new ChasterTabView();
            window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var hero = view.FindControl<Border>("HeroCard")!;
            var facts = view.FindControl<Panel>("FactRow")!;
            var stat = view.FindControl<Border>("StatTab")!;
            Assert.Equal(0, hero.Opacity);
            Assert.All(facts.Children, c => Assert.Equal(0, c.Opacity));
            Assert.Equal(1, stat.Opacity);
            clock.Now += TimeSpan.FromMilliseconds(100).Ticks;
            view.StepArrival();
            Assert.True(hero.Opacity > facts.Children.First().Opacity);
            clock.Now += TimeSpan.FromSeconds(1).Ticks;
            view.StepArrival();
            Assert.All(facts.Children, c => Assert.Equal(1, c.Opacity));
            view.IsVisible = false;
            facts.IsVisible = false;
            view.FindControl<Control>("NumbersPanel")!.IsVisible = true;
            stat.IsVisible = false;
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, hero.Opacity);
            Assert.Equal(1, stat.Opacity);
            Assert.Equal(0, view.FindControl<Border>("StatToday")!.Opacity);
            Assert.All(facts.Children, c => Assert.Equal(1, c.Opacity));
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            view.StepArrival();
            Assert.Equal(1, hero.Opacity);
            Assert.All(facts.Children, c => Assert.Equal(1, c.Opacity));
        }
        finally
        {
            window?.Close();
            ChasterTabView.ArrivalTime = TimeProvider.System;
            CoreSettings.Current.MotionLevel = old;
        }
    });

    private sealed class Clock : TimeProvider
    {
        internal long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
