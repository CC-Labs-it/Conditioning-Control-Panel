using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SubliminalSharedFrameTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ms;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ms;
    }

    [Fact]
    public Task ConcurrentCardsShareThePumpAndReleaseTheirSubscriptions() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        (service.Current.GentleVisuals, service.Current.GentleFadeSeconds) = (true, 0.25);
        var clock = new Clock();
        var oldClock = SubliminalOverlayWindow.Clock;
        var oldFrame = SubliminalOverlayWindow.RequestFrame;
        SubliminalOverlayWindow.Clock = clock;
        SubliminalOverlayWindow.RequestFrame = (_, callback) => callback(TimeSpan.Zero);
        var subscribers = (List<Action<TimeSpan>>)typeof(DispatcherFrameSource)
            .GetField("_subs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(DispatcherFrameSource.Shared)!;
        var before = subscribers.Count;
        var first = new SubliminalOverlayWindow("ONE");
        var second = new SubliminalOverlayWindow("TWO");
        try
        {
            first.Show(); second.Show();
            first.Run(0.6, TimeSpan.FromMilliseconds(100));
            second.Run(0.6, TimeSpan.FromMilliseconds(500));
            Assert.Equal(before + 2, subscribers.Count);
            var queuedSecondTick = subscribers[^1];
            Assert.True(first.IsFading); Assert.True(second.IsFading);

            clock.Ms = 600;
            first.FadeFrame(); // natural completion
            Assert.False(first.IsVisible);
            Assert.False(first.IsFading);
            Assert.True(second.IsFading);
            Assert.Equal(before + 1, subscribers.Count);
            queuedSecondTick(TimeSpan.Zero);
            Assert.Equal(0.6, second.CurrentOpacity, 3);

            second.Close(); // Stop or replacement
            Assert.False(second.IsFading);
            Assert.Equal(before, subscribers.Count);
            clock.Ms = 1000;
            queuedSecondTick(TimeSpan.Zero); // a source snapshot can outlive the unsubscribe
            Assert.Equal(0.6, second.CurrentOpacity, 3);
            return Task.CompletedTask;
        }
        finally
        {
            first.Close(); second.Close();
            SubliminalOverlayWindow.Clock = oldClock;
            SubliminalOverlayWindow.RequestFrame = oldFrame;
        }
    });
}
