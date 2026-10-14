using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HapticMockToastTests
{
    [Fact]
    public Task ProviderFeedbackReusesOneWindowRefreshesExpiryAndPanicDropsQueuedPosts() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = MockProviderV2.Toast;
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
            await Task.Delay(600);
            // Mixer callbacks arrive from a worker thread, never the window thread.
            await Task.Run(() => mock.StopAllAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, HapticMockToast.Window);
            Assert.Equal("Mock haptics: ALL STOP", Assert.IsType<TextBlock>(first.Content).Text);
            await Task.Delay(600); // past original expiry, before refreshed expiry
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, HapticMockToast.Window);
            await Task.Delay(600);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(HapticMockToast.Window);

            HapticMockToast.Post("running");
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(HapticMockToast.Window);
            HapticMockToast.Post("queued before panic");
            // Exercise the real registered stop without stopping unrelated global test services.
            foreach (var surface in PanicSurfaces.All)
                if (surface.Id == "haptics-mock-toast") surface.Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(HapticMockToast.Window);
            await mock.StopAllAsync(); // late mixer retry must stay closed
            Dispatcher.UIThread.RunJobs();
            Assert.Null(HapticMockToast.Window);
            HapticMockToast.Post("Lush V50%");
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(HapticMockToast.Window);
        }
        finally
        {
            HapticMockToast.Close();
            MockProviderV2.Toast = old;
        }
    });
}
