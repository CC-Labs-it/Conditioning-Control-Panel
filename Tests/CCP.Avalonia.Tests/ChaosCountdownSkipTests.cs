using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosCountdownSkipTests
{
    private static Task Dispatch(Action action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        action();
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
            global::Avalonia.Threading.DispatcherPriority.Background);
    });

    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static int Subscribers(Type type, string name) =>
        ((Delegate?)type.GetField(name, System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    [Fact]
    public async Task Finish_and_close_release_both_listener_subscriptions()
    {
        await Dispatch(() =>
        {
            Setup();
            var x = Subscribers(typeof(X11PanicKey), "CountdownPress");
            var w = Subscribers(typeof(Win32PanicKey), "KeyDown");
            var overlay = ChaosOverlayWindow.ForRun();
            try
            {
                overlay.Show();
                overlay.ShowCountdown(() => { });
                Assert.Equal(x + 1, Subscribers(typeof(X11PanicKey), "CountdownPress"));
                Assert.Equal(w + 1, Subscribers(typeof(Win32PanicKey), "KeyDown"));
                overlay.FinishCountdown();
                Assert.Equal(x, Subscribers(typeof(X11PanicKey), "CountdownPress"));
                Assert.Equal(w, Subscribers(typeof(Win32PanicKey), "KeyDown"));
                overlay.ShowReadyGo(() => { });
                overlay.Close();
                Assert.Equal(x, Subscribers(typeof(X11PanicKey), "CountdownPress"));
                Assert.Equal(w, Subscribers(typeof(Win32PanicKey), "KeyDown"));
            }
            finally { overlay.Close(); }
        });
    }

    // WPF ChaosOverlayWindow.xaml.cs:154-168: any key, left/right down only.
    [Theory]
    [InlineData(-1, 65, true)]
    [InlineData(13, 42, true)]
    [InlineData(15, 1, true)]
    [InlineData(15, 3, true)]
    [InlineData(15, 2, false)]
    [InlineData(14, 42, false)]
    [InlineData(16, 1, false)]
    public async Task Desktop_press_skips_once_and_detaches(int type, int detail, bool skips)
    {
        ChaosOverlayWindow? overlay = null;
        var completed = 0;
        try
        {
            await Dispatch(() =>
            {
                Setup();
                overlay = ChaosOverlayWindow.ForRun();
                overlay.Show();
                overlay.ShowCountdown(() => completed++);
                if (type == -1)
                {
                    Win32PanicKey.OnDown(detail, DateTime.UtcNow);
                    Win32PanicKey.OnUp(detail);
                }
                else
                {
                    X11PanicKey.RouteCountdownPress(type, detail);
                    X11PanicKey.RouteCountdownPress(type, detail);
                }
            });
            await Dispatch(() =>
            {
                Assert.Equal(skips ? 1 : 0, completed);
                Assert.Equal(!skips, overlay!.FindControl<Viewbox>("CountdownBox")!.IsVisible);
                overlay.FinishCountdown();
                X11PanicKey.RouteCountdownPress(13, 42);
            });
            await Dispatch(() => Assert.Equal(1, completed));
        }
        finally { await Dispatch(() => overlay?.Close()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_press_cannot_finish_a_closed_or_replaced_countdown(bool replace)
    {
        ChaosOverlayWindow? overlay = null;
        var completed = 0;
        try
        {
            await Dispatch(() =>
            {
                Setup();
                overlay = ChaosOverlayWindow.ForRun();
                overlay.Show();
                overlay.ShowCountdown(() => completed++);
                X11PanicKey.RouteCountdownPress(13, 42);
                if (replace) overlay.ShowReadyGo(() => completed++);
                else overlay.Close();
            });
            await Dispatch(() =>
            {
                Assert.Equal(0, completed);
                if (replace)
                {
                    X11PanicKey.RouteCountdownPress(15, 1);
                }
            });
            await Dispatch(() => Assert.Equal(replace ? 1 : 0, completed));
        }
        finally { await Dispatch(() => overlay?.Close()); }
    }
}
