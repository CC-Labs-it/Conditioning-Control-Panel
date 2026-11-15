using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BouncingTextScrawlTests
{
    [Fact]
    public async Task Panic_stops_standalone_bouncing_text_even_without_an_engine()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests);
            var host = new Window();
            try
            {
                host.Show();
                BouncingTextOverlay.SkipPlatformChecksForTests = true;
                BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.Start(host);
                Assert.True(BouncingTextOverlay.IsRunning);
                Assert.Single(PanicSurfaces.All, s => s.Id == "bouncing-text").Stop(null);
                Assert.False(BouncingTextOverlay.IsRunning);
            }
            finally
            {
                BouncingTextOverlay.Stop();
                host.Close();
                (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
