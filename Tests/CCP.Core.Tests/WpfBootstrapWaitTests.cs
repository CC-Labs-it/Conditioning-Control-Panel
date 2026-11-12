using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Tests;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WpfBootstrapWaitTests
{
    [Fact]
    public void TimedOutCallerDoesNotStartASecondBootstrap()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        var startup = new WpfBootstrapWait(() => { Interlocked.Increment(ref starts); return completion.Task; });
        var error = Assert.Throws<InvalidOperationException>(() => startup.Wait(TimeSpan.Zero));
        Assert.Contains("timed out", error.Message);
        using var entered = new ManualResetEventSlim();
        var second = Task.Run(() => { entered.Set(); startup.Wait(TimeSpan.FromSeconds(10)); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(second.Wait(TimeSpan.FromMilliseconds(50)));
        completion.SetResult();
        second.GetAwaiter().GetResult();
        Assert.Equal(1, starts);
    }

    [Fact]
    public void BootstrapFailureIsSharedAndKeepsTheOriginalCause()
    {
        int starts = 0;
        var cause = new InvalidOperationException("broken theme");
        var startup = new WpfBootstrapWait(() => { Interlocked.Increment(ref starts); return Task.FromException(cause); });
        for (int i = 0; i < 2; i++)
        {
            var error = Assert.Throws<InvalidOperationException>(() => startup.Wait(TimeSpan.FromSeconds(1)));
            Assert.Contains("bootstrap failed", error.Message);
            Assert.Same(cause, error.InnerException);
        }
        Assert.Equal(1, starts);
    }
}
