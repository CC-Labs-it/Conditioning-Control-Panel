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
        // The target is parked in a kernel wait BEFORE the capture starts, the shape the watchdog
        // sees (another thread snapshotting a UI thread that is not moving). This used to snapshot
        // the calling thread itself, which is still running while the worker takes the snapshot:
        // caught in native code with no transition frame (MRES.Wait's spin phase), the DAC walk
        // starts at the next explicit frame and every frame above RuntimeMethodHandle.InvokeMethod
        // vanished. That failed twice on loaded Windows runners (flakes.md, 2026-10-13).
        // WaitSleepJoin alone is set before the OS wait starts; snapshot only once Windows reports
        // a user-requested kernel wait, so the target cannot be caught entering the wait.
        using var release = new ManualResetEvent(false);
        int nativeThreadId = 0;
        var parked = new Thread(() => ParkedInAKnownFrame(release, ref nativeThreadId))
            { IsBackground = true, Name = "ParkedTarget" };
        parked.Start();
        try
        {
            var deadline = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            while (true)
            {
                var id = Volatile.Read(ref nativeThreadId);
                process.Refresh();
                bool waiting = false;
                foreach (ProcessThread thread in process.Threads)
                {
                    using (thread)
                    {
                        if (thread.Id == id && thread.ThreadState == System.Diagnostics.ThreadState.Wait
                            && thread.WaitReason == ThreadWaitReason.UserRequest)
                            waiting = true;
                    }
                }
                if (waiting) break;
                Assert.True(deadline.ElapsedMilliseconds < 10_000, "target thread never reached a kernel wait");
                Thread.Sleep(1);
            }

            var sw = Stopwatch.StartNew();
            var text = UiThreadStacks.CaptureWithBudget(parked.ManagedThreadId, 90_000);
            sw.Stop();

            // The parked method's own frame must appear: that is a managed stack walk having
            // worked, not a placeholder string. Every report in the freeze family is missing this.
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
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // NoInlining: the assertion names this frame, so the JIT must not fold it into the lambda.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParkedInAKnownFrame(ManualResetEvent release, ref int nativeThreadId)
    {
        Volatile.Write(ref nativeThreadId, (int)GetCurrentThreadId());
        release.WaitOne();
        GC.KeepAlive(release);
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
