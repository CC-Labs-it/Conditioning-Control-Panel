using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellMemoryTests(ITestOutputHelper output)
{
    [Fact]
    public Task ClosedShellsReleaseMemory() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.AvatarEnabled = false;
        var windows = new WeakReference[6];
        for (var i = 0; i < windows.Length; i++)
        {
            windows[i] = OpenAndClose();
            await Task.Yield();
            // Finalizers/compositor disposal can queue another dispatcher callback.
            // Drain those as well; no real-time sleeps or RSS thresholds.
            for (var pass = 0; pass < 3; pass++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            output.WriteLine($"iteration {i}: rss={process.WorkingSet64 / 1048576} MiB managed={GC.GetTotalMemory(false) / 1048576} MiB alive={Array.FindAll(windows, w => w?.IsAlive == true).Length}");
        }
        // Object liveness, not an allocator/OS-dependent RSS threshold, is the regression guard.
        Assert.All(windows, w => Assert.False(w.IsAlive, "A closed shell is still rooted."));
    });

    [Fact]
    public Task AnAmbientLoopDoesNotKeepItsClosedWindow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        // Ticking, and parked: a loop that starts on a hidden owner (a breath on a hidden tab).
        var windows = new[] { OpenLoopAndClose(parked: false), OpenLoopAndClose(parked: true) };
        await Task.Yield();
        for (var pass = 0; pass < 3; pass++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
        Assert.False(windows[0].IsAlive, "A closed window with a BeatLoop is still rooted (frame clock / BeatLoop.Live).");
        Assert.False(windows[1].IsAlive, "A closed window with a parked BeatLoop is still rooted (BeatLoop.Live).");
    });

    // The loop is never stopped by its owner, and a late restart after Close (headless posts
    // Activated, which re-applies the dashboard loops) must not re-arm it either.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenLoopAndClose(bool parked)
    {
        var text = new global::Avalonia.Controls.TextBlock { Text = "beat", IsVisible = !parked };
        var window = new global::Avalonia.Controls.Window { Content = text };
        window.Show();
        var loop = new global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop(text, t => text.Opacity = t % 1);
        loop.Start();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(!parked, loop.IsTicking);
        Assert.True(loop.IsRunning);
        window.Close();
        Assert.False(loop.IsRunning);
        loop.Start();
        Assert.False(loop.IsTicking);
        return new WeakReference(window);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndClose()
    {
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        // To the tray and back: Opened fires on every Show, Closed once, so a static subscription
        // made in Opened without a matching -= outlives the window (WebcamTracker, settings service).
        shell.Hide();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        shell.Close();
        Assert.True(closed);
        return new WeakReference(shell);
    }
}
