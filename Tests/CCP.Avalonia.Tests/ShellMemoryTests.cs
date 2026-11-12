using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Tours;
using Xunit;

namespace CCP.Avalonia.Tests;

// Shared dispatcher/settings snapshots must remain exclusive across async lifecycle turns.
[Collection(RunsAloneCollection.Name)]
public sealed class ShellMemoryTests(ITestOutputHelper output)
{
    [Fact]
    public Task ClosedShellsReleaseMemory() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.AvatarEnabled = false;
        var windows = new WeakReference[6];
        for (var i = 0; i < windows.Length; i++)
        {
            windows[i] = OpenAndClose();
            await LifecycleTestPump.UntilAsync(() => !windows[i].IsAlive,
                "A closed shell is still rooted.", collect: true);
            using var process = Process.GetCurrentProcess();
            output.WriteLine($"iteration {i}: rss={process.WorkingSet64 / 1048576} MiB managed={GC.GetTotalMemory(false) / 1048576} MiB alive={Array.FindAll(windows, w => w?.IsAlive == true).Length}");
        }
        // Object liveness, not an allocator/OS-dependent RSS threshold, is the regression guard.
        Assert.All(windows, w => Assert.False(w.IsAlive, "A closed shell is still rooted."));
    });

    [Fact]
    public Task AFinishedTourDoesNotKeepItsClosedShell() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.AvatarEnabled = false;
        CoreSettings.Current.PerformanceMode = true;
        await RunTourLeakCheck(shell => LifecycleTestPump.UntilAsync(() => !shell.IsAlive,
            "A finished tour still roots its closed shell.", collect: true));
    });

    private static readonly FieldInfo[] TutorialStateFields = typeof(TutorialService)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static object?[] CaptureTutorialState() => TutorialStateFields
        .Select(field => field.GetValue(TutorialHead.Service)).ToArray();

    private static void RestoreTutorialState(object?[] state)
    {
        for (var i = 0; i < state.Length; i++) TutorialStateFields[i].SetValue(TutorialHead.Service, state[i]);
    }

    private static async Task RunTourLeakCheck(Func<WeakReference, Task> check)
    {
        // Seed before the snapshot: the singleton's event wiring and _seeded must stay in sync.
        TutorialHead.Seed();
        var state = CaptureTutorialState();
        try { await check(OpenTourAndClose()); }
        finally { RestoreTutorialState(state); }
    }

    [Fact]
    public Task TourLeakCheckRestoresSingletonStateEvenWhenAssertionFails() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.AvatarEnabled = false;
        CoreSettings.Current.PerformanceMode = true;
        TutorialHead.Seed();
        var original = CaptureTutorialState();
        try
        {
            var calls = 0;
            void Navigate() => calls++;
            TutorialHead.Service.ConfigureCallbacks(Navigate, Navigate, Navigate, Navigate, Navigate, Navigate,
                Navigate, Navigate, _ => calls++);
            TutorialHead.Service.Start(TutorialType.ShortWalk);
            TutorialHead.Service.Next();
            var expected = CaptureTutorialState();

            await RunTourLeakCheck(shell => LifecycleTestPump.UntilAsync(() => !shell.IsAlive,
                "A finished tour still roots its closed shell.", collect: true));
            Assert.Equal(expected, CaptureTutorialState());
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunTourLeakCheck(_ =>
                Task.FromException(new InvalidOperationException("assertion failed"))));
            Assert.Equal(expected, CaptureTutorialState());
            var before = calls;
            TutorialHead.Service.CurrentSteps.First(step => step.RequiresTab != null).OnActivate!();
            Assert.True(calls > before, "The original navigation callbacks were restored.");
        }
        finally { RestoreTutorialState(original); }
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenTourAndClose()
    {
        var previous = TutorialHeadHooks.ShellProvider;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            TutorialHead.StartOnShell(TutorialType.ShortWalk);
            Assert.True(TutorialHead.Service.IsActive);
            TutorialHead.Service.Skip();
        }
        finally
        {
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = previous;
            shell.Close();
        }
        return new WeakReference(shell);
    }

    [Fact]
    public Task AnAmbientLoopDoesNotKeepItsClosedWindow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        // Ticking, and parked: a loop that starts on a hidden owner (a breath on a hidden tab).
        var windows = new[] { OpenLoopAndClose(parked: false), OpenLoopAndClose(parked: true) };
        await LifecycleTestPump.UntilAsync(() => Array.TrueForAll(windows, w => !w.IsAlive),
            "A closed ambient-loop window is still rooted.", collect: true);
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
