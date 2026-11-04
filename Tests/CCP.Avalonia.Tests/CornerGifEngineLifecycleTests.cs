using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CornerGifEngineLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now = TimeSpan.FromSeconds(10).Ticks;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopCancelsPendingAndShownSlotsAndStartRestoresThem(bool realizeSecond)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-gif-engine-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7"));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var service = new SettingsService();
                var previous = CoreSettings.ServiceProvider;
                CoreSettings.ServiceProvider = () => service;
                service.Current.PanicKeyEnabled = false;
                service.Current.CornerGifOverlays = new List<CornerGifOverlaySetting> {
                    new() { Enabled = true, GifPath = gif }, new() { Enabled = true, GifPath = gif } };
                var clock = new Clock();
                var shell = new MainShellWindow();
                shell.Show();
                try
                {
                    CornerGifOverlay.StopAll();
                    CornerGifOverlay.Clock = clock;
                    CornerGifOverlay.SkipPlatformChecksForTests = true;
                    CornerGifOverlay.ManualFramesForTests = true;
                    CoreEngine.StoppedHook = shell.OnEngineStopped;
                    shell.StartEngine();
                    await Settle();
                    Assert.Equal(new[] { 0 }, CornerGifOverlay.ShownSlots);
                    if (realizeSecond)
                    {
                        clock.Now += TimeSpan.FromSeconds(1).Ticks;
                        CornerGifOverlay.Pump();
                        await Settle();
                        Assert.Equal(new[] { 0, 1 }, CornerGifOverlay.ShownSlots.OrderBy(x => x));
                    }
                    MainShellWindow.StopEngine();
                    clock.Now += TimeSpan.FromSeconds(2).Ticks;
                    CornerGifOverlay.Pump();
                    await Settle();
                    Assert.Empty(CornerGifOverlay.ShownSlots);
                    Assert.False(CornerGifOverlay.HasActiveOverlays);
                    shell.StartEngine();
                    await Settle();
                    clock.Now += TimeSpan.FromSeconds(1).Ticks;
                    CornerGifOverlay.Pump();
                    await Settle();
                    Assert.Equal(new[] { 0, 1 }, CornerGifOverlay.ShownSlots.OrderBy(x => x));
                }
                finally
                {
                    CoreEngine.Stop();
                    CoreEngine.StoppedHook = null;
                    CornerGifOverlay.StopAll();
                    CornerGifOverlay.Clock = TimeProvider.System;
                    CornerGifOverlay.SkipPlatformChecksForTests = false;
                    CornerGifOverlay.ManualFramesForTests = false;
                    CoreSettings.ServiceProvider = previous;
                    shell.Close();
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            while (CornerGifOverlay.LastDecode is { IsCompleted: false } d) await d;
            Dispatcher.UIThread.RunJobs();
        }
    }
}
