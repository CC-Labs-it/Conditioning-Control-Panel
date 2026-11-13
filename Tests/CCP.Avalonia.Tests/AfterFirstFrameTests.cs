using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AfterFirstFrameTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Setup()
    {
        if (Application.Current is null)
            Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task WaitsForRenderAndRunsOnceAcrossTrayRestore() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var shell = new Window();
        var requested = Signal();
        var rendered = Signal();
        var calls = 0;
        var done = AfterFirstFrame.Schedule(shell, () =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            calls++;
            return Task.CompletedTask;
        }, () => shell.IsVisible ? shell : null, w =>
        {
            Assert.Same(shell, w);
            requested.TrySetResult();
            return rendered.Task;
        });
        try
        {
            Assert.Equal(0, calls);
            shell.Show();
            await requested.Task.WaitAsync(Timeout);
            Assert.Equal(0, calls); // Opened and the animation callback alone are not a rendered frame.
            Assert.False(done.IsCompleted);
            rendered.SetResult();
            await done.WaitAsync(Timeout);
            Assert.Equal(1, calls);
            shell.Hide();
            shell.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, calls);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task ShellCloseCancelsAnUnacknowledgedFrame() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var shell = new Window();
        var requested = Signal();
        var rendered = Signal();
        var calls = 0;
        var done = AfterFirstFrame.Schedule(shell, () => { calls++; return Task.CompletedTask; },
            () => shell.IsVisible ? shell : null, _ => { requested.TrySetResult(); return rendered.Task; });
        try
        {
            shell.Show();
            await requested.Task.WaitAsync(Timeout);
            shell.Close();
            await done.WaitAsync(Timeout); // Must finish even if the renderer never acknowledges.
            rendered.SetResult(); // A stale acknowledgement must not resurrect cancelled work.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(0, calls);
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public Task DisappearingLauncherReselectsOrUsesNoFrameFallback(bool close, bool replacement) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var shell = new Window();
        var launcher = new Window();
        var game = new Window();
        var firstRequested = Signal();
        var secondRequested = Signal();
        var firstFrame = Signal();
        var secondFrame = Signal();
        var calls = 0;
        // Same Opened routing as boot: the panel is hidden before the deferred selection runs.
        shell.Opened += (_, _) => { launcher.Show(); shell.Hide(); };
        var done = AfterFirstFrame.Schedule(shell, () => { calls++; return Task.CompletedTask; },
            () => launcher.IsVisible ? launcher : game.IsVisible ? game : null, w =>
            {
                if (w == launcher) { firstRequested.TrySetResult(); return firstFrame.Task; }
                Assert.Same(game, w);
                secondRequested.TrySetResult();
                return secondFrame.Task;
            });
        try
        {
            shell.Show();
            await firstRequested.Task.WaitAsync(Timeout);
            Assert.Equal(0, calls);
            if (replacement) game.Show();
            if (close) launcher.Close(); else launcher.Hide();
            if (replacement)
            {
                await secondRequested.Task.WaitAsync(Timeout);
                firstFrame.SetResult(); // The dead launcher's frame cannot release the replacement's gate.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, calls);
                secondFrame.SetResult();
            }
            await done.WaitAsync(Timeout); // With no replacement the no-frame fallback must run.
            Assert.Equal(1, calls);
            firstFrame.TrySetResult();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, calls);
        }
        finally { launcher.Close(); game.Close(); shell.Close(); }
    });
}
