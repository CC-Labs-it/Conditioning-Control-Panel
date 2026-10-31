using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FloatingVideoRevealTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task No_pixels_or_clicks_until_a_frame_and_stop_cannot_be_undone_by_a_late_frame()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var window = new Window { ShowActivated = false };
            var reveal = new FloatingVideoReveal(window);
            try
            {
                Assert.False(window.IsVisible);
                reveal.Present();
                Assert.True(window.IsVisible);
                Assert.False(window.ShowActivated);
                reveal.Present();
                Assert.True(window.IsVisible);
                reveal.Close();
                reveal.Present();
                Assert.False(window.IsVisible);

                var stopped = new Window();
                var hidden = new FloatingVideoReveal(stopped);
                hidden.Close();
                hidden.Present();
                Assert.False(stopped.IsVisible);
            }
            finally { reveal.Close(); }
            return Task.CompletedTask;
        });
    }
}
