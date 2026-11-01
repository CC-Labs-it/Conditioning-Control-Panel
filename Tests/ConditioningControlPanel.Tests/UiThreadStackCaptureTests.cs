using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The capture route itself (ccp-bugs #1189 / #1179 / #1159 / #984). Everything else in this
/// change is formatting; this is the part that either produces frames or does not, and the only
/// way to be sure a wedged 6.9.4 will produce them is to take a real snapshot of a real process
/// here. It runs against the app assembly, so it also proves the pinned ClrMD package actually
/// resolves at runtime rather than merely compiling.
///
/// It cannot reproduce a WPF UI hang (no message loop in a test host) — it proves the mechanism:
/// snapshot this process from another thread, find a named managed thread in it, read its frames.
/// A hung UI thread is strictly EASIER than this, because it is not moving.
/// </summary>
public class UiThreadStackCaptureTests
{
    public UiThreadStackCaptureTests() => UiThreadStacks.ResetAbandonedLatchForTests();

    [Fact]
    public void AnAbandonedCaptureLatchesSoTheSessionNeverStacksSnapshots()
    {
        var first = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 0);
        Assert.Contains("budget", first);
        var second = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 90_000);
        Assert.Contains("skipped", second);
    }

    [Fact]
    public void ASnapshotYieldsAParkedThreadsManagedFrames()
    {
        // WaitSleepJoin can be set before the thread has entered the kernel wait. Do not take
        // the snapshot while the runtime is still transitioning into WaitOne: observe the OS
        // thread in a wait as well, after it has reported its native id.
        using var release = new ManualResetEvent(false);
        using var started = new ManualResetEventSlim();
        uint nativeId = 0;
        int afterWait = 0;
        var parked = new Thread(() =>
        {
            nativeId = GetCurrentThreadId();
            started.Set();
            ParkedInAKnownFrame(release, () => Interlocked.Exchange(ref afterWait, 1));
        }) { IsBackground = true, Name = "ParkedTarget" };
        parked.Start();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "target thread never started");
            var deadline = Stopwatch.StartNew();
            while ((parked.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0 || !NativeThreadIsWaiting(nativeId))
            {
                Assert.True(deadline.ElapsedMilliseconds < 10_000, "target thread never parked in the OS wait");
                Thread.Yield();
            }

            var sw = Stopwatch.StartNew();
            var text = UiThreadStacks.CaptureWithBudget(parked.ManagedThreadId, 90_000);
            sw.Stop();

            // The parked method's own frame must appear: that is a managed stack walk having
            // worked, not a placeholder string. Every report in the freeze family is missing this.
            Assert.True(text.Contains(nameof(ParkedInAKnownFrame)), "Missing parked frame in snapshot:\n" + text);
            Assert.Contains(nameof(ParkedInAKnownFrame), text);
            Assert.Contains("UI thread", text);

            // The watchdog runs this on a process that is already in trouble. Measured at ~100ms; the
            // assert is loose because it shares a machine with the rest of the suite, but a capture
            // that took seconds would mean the snapshot route regressed into something unshippable.
            Assert.True(sw.ElapsedMilliseconds < 60_000,
                $"stack capture took {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            release.Set();
            parked.Join();
        }
        Assert.Equal(1, afterWait);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static bool NativeThreadIsWaiting(uint threadId)
    {
        using var process = Process.GetCurrentProcess();
        foreach (ProcessThread thread in process.Threads)
        {
            if (thread.Id == (int)threadId) return thread.ThreadState == System.Diagnostics.ThreadState.Wait;
        }
        return false;
    }

    // NoInlining and work after WaitOne keep the named frame live in optimized builds.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParkedInAKnownFrame(ManualResetEvent release, Action afterWait)
    {
        release.WaitOne();
        afterWait();
    }

    [Fact]
    public void AnUnknownThreadIdDegradesInsteadOfThrowing()
    {
        // SafeUiManagedThreadId returns -1 when the dispatcher will not answer, and a wedged
        // process is exactly where that happens. The report must still get a printable line.
        var text = UiThreadStacks.CaptureWithBudget(-1, 90_000);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("not present in the snapshot", text);
    }

    [Fact]
    public void AnImpossibleBudgetIsAbandonedRatherThanWaitedOut()
    {
        // The rule the whole design turns on: a hang capture that itself hangs is worse than no
        // capture. Zero budget is the degenerate case of that promise.
        var sw = Stopwatch.StartNew();
        var text = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 0);
        sw.Stop();

        Assert.Contains("budget", text);
        Assert.True(sw.ElapsedMilliseconds < 30_000, $"gave up after {sw.ElapsedMilliseconds}ms");
    }
}
