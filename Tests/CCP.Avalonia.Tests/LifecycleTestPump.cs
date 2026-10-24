using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class LifecycleTestPump
{
    // Frame ticks request work; they neither advance the animation clock nor guarantee that
    // compositor/finalizer continuations have returned. Yield the UI thread between probes.
    // The deadline is a hang guard, not a delay or an assertion tolerance: return on completion.
    internal static async Task UntilAsync(Func<bool> done, string failure, bool collect = false)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (collect)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            if (done()) return;
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), failure);
            await Task.Delay(1);
        }
    }
}
