using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Input;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Panic leftovers from WPF 7.1.5 (parity wave 2, platform lane): the #919b watchdog tears
/// down off-thread when the queued handler never runs; an Escape aimed at a marked surface is the
/// surface's once, never twice in a row; the optional Pause key never shadows the panic key.</summary>
[Collection("Win32PanicKey")]
public sealed class PanicLeftoversTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public Task EmergencyTeardownStopsAudioPlaylist() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var slot = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("AudioPlaylist")!;
        var previous = slot.GetValue(null);
        var playlist = new AudioPlaylistCardModelTests.FakePlaylist { IsPlaying = true };
        slot.SetValue(null, playlist);
        try
        {
            await Task.Run(PanicWatchdog.Teardown);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.False(playlist.IsPlaying);
        }
        finally { slot.SetValue(null, previous); }
    });

    [Fact]
    public async Task WatchdogLeavesAThrownHandlerAlone()
    {
        // WPF's watchdog only observes a fault, and the fallback is for a UI thread that never answered.
        var old = PanicWatchdog.Teardown;
        var fired = 0;
        PanicWatchdog.Teardown = () => Interlocked.Increment(ref fired);
        try
        {
            await PanicWatchdog.Arm(Task.FromException(new InvalidOperationException("test")), TimeSpan.FromSeconds(30));
            Assert.Equal(0, fired);
        }
        finally { PanicWatchdog.Teardown = old; }
    }

    [Fact]
    public async Task WatchdogTearsDownOnlyWhenTheHandlerNeverRuns()
    {
        var old = PanicWatchdog.Teardown;
        var fired = 0;
        PanicWatchdog.Teardown = () => Interlocked.Increment(ref fired);
        try
        {
            await PanicWatchdog.Arm(Task.CompletedTask, TimeSpan.FromMilliseconds(50));
            Assert.Equal(0, fired);

            var never = new TaskCompletionSource().Task;   // a wedged UI thread
            await PanicWatchdog.Arm(never, TimeSpan.FromMilliseconds(50));
            Assert.Equal(1, fired);
        }
        finally { PanicWatchdog.Teardown = old; }
    }

    [Fact]
    public async Task AHandlerThatThrowsAfterTheFallbackIsStillLogged()
    {
        var old = PanicWatchdog.Teardown;
        PanicWatchdog.Teardown = () => { };
        var previous = Serilog.Log.Logger;
        var logged = new TaskCompletionSource();
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(new FaultSink(logged)).CreateLogger();
        try
        {
            var handler = new TaskCompletionSource();
            await PanicWatchdog.Arm(handler.Task, TimeSpan.FromMilliseconds(50));
            handler.SetException(new InvalidOperationException("late"));
            Assert.Same(logged.Task, await Task.WhenAny(logged.Task, Task.Delay(TimeSpan.FromSeconds(5))));
        }
        finally
        {
            Serilog.Log.Logger = previous;
            PanicWatchdog.Teardown = old;
        }
    }

    private sealed class FaultSink(TaskCompletionSource logged) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent entry)
        {
            if (entry.Exception?.ToString().Contains("late") == true) logged.TrySetResult();
        }
    }

    [Fact]
    public async Task WatchdogStaysOutOfAShutdown()
    {
        var old = PanicWatchdog.Teardown;
        var fired = 0;
        PanicWatchdog.Teardown = () => Interlocked.Increment(ref fired);
        PanicWatchdog.ShutdownStartedForTest = true;   // WPF HasShutdownStarted (MainWindow.xaml.cs:1073)
        try
        {
            await PanicWatchdog.Arm(new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(50));
            Assert.Equal(0, fired);
        }
        finally { PanicWatchdog.ShutdownStartedForTest = false; PanicWatchdog.Teardown = old; }
    }

    [Fact]
    public void EscapeInAMarkedSurfaceIsTakenOnceThenPanics()
    {
        EscapeClaim.Taken();
        EscapeClaim.SetKeyboardInSurfaceForTest(true);
        try
        {
            // Esc is the panic key, CCP in front, the drawer has the keyboard: the drawer's.
            Assert.True(EscapeClaim.TakenBySurface(true, "Escape", lockCardOpen: false, T0, ccpInFront: true));
            // A second Esc while the first is still on its way panics (never two in a row).
            Assert.False(EscapeClaim.TakenBySurface(true, "Escape", false, T0.AddMilliseconds(300), true));
            EscapeClaim.Taken();
            // Never with a lock card up, another app in front, or a panic key that is not Escape.
            Assert.False(EscapeClaim.TakenBySurface(true, "Escape", lockCardOpen: true, T0, ccpInFront: true));
            Assert.False(EscapeClaim.TakenBySurface(true, "Escape", false, T0, ccpInFront: false));
            Assert.False(EscapeClaim.TakenBySurface(true, "F24", false, T0, ccpInFront: true));
            EscapeClaim.SetKeyboardInSurfaceForTest(false);
            Assert.False(EscapeClaim.TakenBySurface(true, "Escape", false, T0, ccpInFront: true));
        }
        finally { EscapeClaim.SetKeyboardInSurfaceForTest(false); EscapeClaim.Taken(); }
    }

    [Fact]
    public Task MarkedSurfaceIsFoundFromAChildAndByType() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var box = new TextBox();
        var surface = new Border { Child = new StackPanel { Children = { box } } };
        Assert.False(EscapeClaim.InASurface(box));
        EscapeClaim.Mark(surface);
        Assert.True(EscapeClaim.InASurface(box));

        var typed = new TextBox();
        var holder = new MarkedByType { Child = typed };
        Assert.False(EscapeClaim.InASurface(typed));
        EscapeClaim.MarkType(typeof(MarkedByType));
        Assert.True(EscapeClaim.InASurface(typed));
        _ = holder;
        return Task.CompletedTask;
    });

    private sealed class MarkedByType : Decorator { }

    [Fact]
    public void PauseKeyIsOnePressAndNeverShadowsThePanicKey()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "Escape";
            s.PauseKey = "F9";
            Win32Input.ResetPauseKeyForTest();
            Assert.True(Win32Input.OnPauseKeyDown(0x78));    // F9
            Assert.False(Win32Input.OnPauseKeyDown(0x78));   // held: one press
            Win32Input.ResetPauseKeyForTest();
            Assert.False(Win32Input.OnPauseKeyDown(0x41));   // another key

            s.PauseKey = "Escape";   // the panic key wins a shared binding
            Win32Input.ResetPauseKeyForTest();
            Assert.False(Win32Input.OnPauseKeyDown(VirtualKeys.Escape));

            s.PauseKey = "";   // unbound by default: nothing
            Win32Input.ResetPauseKeyForTest();
            Assert.False(Win32Input.OnPauseKeyDown(VirtualKeys.Escape));
        }
        finally { CoreSettings.ServiceProvider = oldProvider; Win32Input.ResetPauseKeyForTest(); }
    }
}
