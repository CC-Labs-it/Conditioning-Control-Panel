using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosCountdownInputTests
{
    private sealed class Watch : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Press_skips_full_restart_and_ready_countdowns_once_and_unsubscribes(bool shortFlash)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = ChaosOverlayWindow.WatchCountdownInput;
            var presses = new List<Action>();
            var watches = new List<Watch>();
            ChaosOverlayWindow.WatchCountdownInput = press =>
            {
                presses.Add(press);
                var watch = new Watch(); watches.Add(watch); return watch;
            };
            var win = ChaosOverlayWindow.ForRun();
            int completed = 0;
            try
            {
                win.Show();
                win.ShowCountdown(() => completed++, shortFlash);
                Assert.Equal(shortFlash ? "SINK" : "3", win.FindControl<TextBlock>("CountdownText")!.Text);
                Assert.Single(presses)();
                presses[0](); // two queued native events still complete only once
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, completed);
                Assert.True(watches[0].Disposed);
                Assert.False(win.FindControl<Viewbox>("CountdownBox")!.IsVisible);

                win.ShowReadyGo(() => completed++);
                Assert.Equal("ready? :3", win.FindControl<TextBlock>("CountdownText")!.Text);
                presses[0](); // stale press cannot skip this countdown
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, completed);
                presses[1]();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, completed);
                Assert.True(watches[1].Disposed);

                win.ShowCountdown(() => completed++);
                presses[2]();
                win.Close(); // panic closes the same run-owned window
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, completed);
                Assert.True(watches[2].Disposed);
            }
            finally { win.Close(); ChaosOverlayWindow.WatchCountdownInput = old; }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(13, 40, true)]
    [InlineData(14, 40, false)]
    [InlineData(15, 1, true)]
    [InlineData(15, 3, true)]
    [InlineData(15, 2, false)]
    [InlineData(15, 4, false)]
    public void X11_routes_only_wpf_skip_presses(int type, int detail, bool expected)
    {
        int presses = 0;
        void Press() => presses++;
        X11PanicKey.CountdownPress += Press;
        try { X11PanicKey.RouteCountdownPress(type, detail); Assert.Equal(expected ? 1 : 0, presses); }
        finally { X11PanicKey.CountdownPress -= Press; }
    }

    [Fact]
    public void Windows_countdown_accepts_any_key_and_options_still_accepts_only_escape()
    {
        Assert.True(Win32OptionsDismiss.AcceptKey(true, 0x41));
        Assert.False(Win32OptionsDismiss.AcceptKey(false, 0x41));
        Assert.True(Win32OptionsDismiss.AcceptKey(false, 0x1b));
    }
}
