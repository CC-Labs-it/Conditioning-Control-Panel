using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HapticMockToastTests
{
    [Fact]
    public Task ProviderFeedbackReusesOneWindowRefreshesExpiryAndExitDropsQueuedPosts() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = MockProviderV2.Toast;
        var oldTime = HapticMockToast.Time;
        var clock = new SteppedClock();
        HapticMockToast.Time = clock;
        using var mock = new MockProviderV2();
        try
        {
            HapticMockToast.Seed();
            await mock.ConnectAsync(default);
            Dispatcher.UIThread.RunJobs();
            var first = HapticMockToast.Window;
            Assert.NotNull(first);
            Assert.True(first.IsVisible);
            Assert.Equal("Mock haptics connected\nLush / Edge / Solace", Assert.IsType<TextBlock>(first.Content).Text);
            Assert.Equal(240, first.Width);
            Assert.Equal(92, first.Height);
            Assert.False(first.ShowInTaskbar);
            Assert.False(first.ShowActivated);
            Assert.True(first.Topmost);
            clock.Now += TimeSpan.FromSeconds(0.6).Ticks;
            // Mixer callbacks arrive from a worker thread, never the window thread. WPF panic reaches
            // the mock through this same StopAll, so ALL STOP stays visible after a panic (WPF MockToast).
            await Task.Run(() => mock.StopAllAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, HapticMockToast.Window);
            Assert.Equal("Mock haptics: ALL STOP", Assert.IsType<TextBlock>(first.Content).Text);
            clock.Now += TimeSpan.FromSeconds(0.6).Ticks; // past original expiry, before refreshed expiry
            HapticMockToast.Tick();
            Assert.Same(first, HapticMockToast.Window);
            clock.Now += TimeSpan.FromSeconds(0.6).Ticks;
            HapticMockToast.Tick();
            Assert.Null(HapticMockToast.Window);

            HapticMockToast.Post("running");
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(HapticMockToast.Window);
            HapticMockToast.Post("queued before exit");
            HapticMockToast.Close();   // App exit
            Dispatcher.UIThread.RunJobs();
            Assert.Null(HapticMockToast.Window);
        }
        finally
        {
            HapticMockToast.Close();
            HapticMockToast.Time = oldTime;
            MockProviderV2.Toast = old;
        }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
