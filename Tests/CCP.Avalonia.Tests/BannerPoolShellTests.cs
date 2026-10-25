using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BannerPoolShellTests
{
    [Fact]
    public async Task PoolJoinsAfterTheLaunchDelayAndOnlySwapsTextWhileFullyHidden()
    {
        await InShell(w =>
        {
            var timer = Field<DispatcherTimer>(w, "_bannerPoolTimer");
            Assert.Equal(TimeSpan.FromSeconds(90), timer.Interval);
            Assert.True(timer.IsEnabled);
            var pool = w.Named<TextBlock>("TxtBannerPool")!;
            Assert.DoesNotContain(pool, Beats(w));
            Draw(w);
            Assert.InRange(timer.Interval.TotalMinutes, 8, 12);
            Assert.Contains(pool, Beats(w));
            var first = pool.Text;
            Assert.False(string.IsNullOrWhiteSpace(first));
            // Join preserves the primary beat currently on screen.
            Assert.Equal(0, Field<int>(w, "_bannerCurrentIndex"));
            ShowPool(w, pool);
            Draw(w);
            var pending = Field<string>(w, "_pendingBannerPoolText");
            Assert.NotEqual(first, pending);
            Assert.Equal(first, pool.Text);
            Rotate(w); // pool -> primary is still a visible crossfade
            Assert.Equal(first, pool.Text);
            Rotate(w); // primary -> secondary, the pool is now fully hidden
            Assert.Equal(pending, pool.Text);
            Assert.Null(Field<string?>(w, "_pendingBannerPoolText"));
            Assert.False(pool.IsHitTestVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DisablingPoolRetiresItsVisibleBeatWithoutBlankingTheOutgoingFade()
    {
        await InShell(w =>
        {
            Draw(w);
            var pool = w.Named<TextBlock>("TxtBannerPool")!;
            ShowPool(w, pool);
            var last = pool.Text;
            CoreSettings.Current.BannerPoolEnabled = false;
            Draw(w);
            Assert.DoesNotContain(pool, Beats(w));
            Assert.Equal(last, pool.Text);
            Assert.Equal(w.Named<TextBlock>("TxtBannerPrimary"), Current(w));
            Assert.True(Current(w).IsHitTestVisible);
            Assert.False(pool.IsHitTestVisible);
            // No skipped index or blank slot after shrink, even when the web beat retires too.
            w.RetireWebBannerBeat();
            for (int i = 0; i < 6; i++)
            {
                Rotate(w);
                Assert.Contains(Current(w), Beats(w));
                Assert.NotSame(pool, Current(w));
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ClosingTheShellStopsBothBannerClocks()
    {
        await InShell(w =>
        {
            var pool = Field<DispatcherTimer>(w, "_bannerPoolTimer");
            var rotation = Field<DispatcherTimer>(w, "_bannerRotationTimer");
            Assert.True(pool.IsEnabled);
            Assert.True(rotation.IsEnabled);
            w.Close();
            Assert.False(pool.IsEnabled);
            Assert.False(rotation.IsEnabled);
            return Task.CompletedTask;
        });
    }

    private static Task InShell(Func<MainShellWindow, Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var enabled = s.BannerPoolEnabled;
        var seen = s.BannerSeenIds.ToList();
        var intros = s.SeenFeatureIntros.ToList();
        MainShellWindow? window = null;
        try
        {
            s.BannerPoolEnabled = true;
            s.BannerSeenIds.Clear();
            s.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
            window = new MainShellWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await action(window);
        }
        finally
        {
            window?.Close();
            s.BannerPoolEnabled = enabled;
            s.BannerSeenIds = seen;
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(intros);
        }
    });

    private static T Field<T>(MainShellWindow w, string name) =>
        (T)typeof(MainShellWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    private static TextBlock[] Beats(MainShellWindow w) => Field<TextBlock[]>(w, "_bannerBeats");
    private static TextBlock Current(MainShellWindow w) => Beats(w)[Field<int>(w, "_bannerCurrentIndex")];
    private static void ShowPool(MainShellWindow w, TextBlock pool)
    {
        for (int i = 0; i < Beats(w).Length && Current(w) != pool; i++) Rotate(w);
        Assert.Same(pool, Current(w));
    }
    private static void Draw(MainShellWindow w) => Tick(w, "BannerPoolTimer_Tick");
    private static void Rotate(MainShellWindow w) => Tick(w, "BannerRotationTimer_Tick");
    private static void Tick(MainShellWindow w, string name) =>
        typeof(MainShellWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, new object?[] { null, EventArgs.Empty });
}
