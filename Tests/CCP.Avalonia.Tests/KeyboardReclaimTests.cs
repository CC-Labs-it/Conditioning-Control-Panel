using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>ccp-bugs #1713: the window manager refuses the card's activation (KWin focus-stealing
/// prevention while the user types elsewhere). The card must keep asking for the keyboard
/// directly, and stop at once on success, completion or panic.</summary>
public sealed class KeyboardReclaimTests
{
    private static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
    }

    private static void PumpUntil(Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(5)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(done(), "timed out");
    }

    private static Task Run(Action<List<Window>, Action<bool>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (native, has, interval) = (KeyboardReclaim.NativeFocus, KeyboardReclaim.HasKeyboard, KeyboardReclaim.Interval);
        var asked = new List<Window>();
        bool granted = false;
        KeyboardReclaim.NativeFocus = w => { asked.Add(w); return false; };
        KeyboardReclaim.HasKeyboard = _ => granted;   // activation refused until granted
        KeyboardReclaim.Interval = TimeSpan.FromMilliseconds(10);
        try { body(asked, g => granted = g); }
        finally
        {
            (KeyboardReclaim.NativeFocus, KeyboardReclaim.HasKeyboard, KeyboardReclaim.Interval) = (native, has, interval);
            LockCardWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefusedLockCardRetriesUntilPanicClosesIt() => Run((asked, _) =>
    {
        LockCardWindow.ShowOnAllMonitors("obey", 2, strictMode: false, isTest: true);
        PumpUntil(() => asked.Count >= 2);
        var primary = LockCardWindow.Primary!;
        Assert.All(asked, w => Assert.Same(primary, w));   // mirrors never take the keyboard

        PanicSurfaces.All.Single(s => s.Id == "lock-cards").Stop(null);
        int afterPanic = asked.Count;
        Pump(80);
        Assert.Equal(afterPanic, asked.Count);
        Assert.False(LockCardWindow.IsAnyOpen());
    });

    [Fact]
    public Task RetryStopsOnceTheCardHasTheKeyboardAndIsBounded() => Run((asked, grant) =>
    {
        LockCardWindow.ShowOnAllMonitors("obey", 2, strictMode: false, isTest: true);
        PumpUntil(() => asked.Count >= 1);
        grant(true);
        Pump(30);
        int n = asked.Count;
        Pump(80);
        Assert.Equal(n, asked.Count);
        Assert.InRange(n, 1, KeyboardReclaim.Attempts);
    });

    [Fact]
    public Task AlwaysRefusedGivesUpAfterTheBoundAndLeavesTheCardUp() => Run((asked, _) =>
    {
        LockCardWindow.ShowOnAllMonitors("obey", 2, strictMode: false, isTest: true);
        PumpUntil(() => asked.Count >= KeyboardReclaim.Attempts);
        Pump(80);
        Assert.Equal(KeyboardReclaim.Attempts, asked.Count);
        Assert.True(LockCardWindow.IsAnyOpen());
    });

    [Fact]
    public Task RefusedBubbleAnswerRetriesAndStopsOnForceClose() => Run((asked, _) =>
    {
        BubbleCountResultWindow.ShowOnAllMonitors(42, strictMode: false, _ => { });
        PumpUntil(() => asked.Count >= 2);
        BubbleCountResultWindow.ForceCloseAll();
        int n = asked.Count;
        Pump(80);
        Assert.Equal(n, asked.Count);
    });
}
