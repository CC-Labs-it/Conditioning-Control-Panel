using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Tests;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WpfBootstrapOnceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimeoutKeepsOneBootstrapAndSharesItsOutcome(bool fail)
    {
        var bootstrap = new BootstrapOnce();
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var starts = 0;
        var failure = new Exception("bootstrap failure");
        void Start(Action<Exception?> complete)
        {
            Interlocked.Increment(ref starts);
            Task.Run(() => { started.Set(); release.Wait(); complete(fail ? failure : null); });
        }

        try
        {
            var timeout = Assert.Throws<InvalidOperationException>(() => bootstrap.Ensure(Start, TimeSpan.FromMilliseconds(100)));
            Assert.Contains("timed out", timeout.Message);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            var second = Assert.Throws<InvalidOperationException>(() => bootstrap.Ensure(Start, TimeSpan.FromMilliseconds(100)));
            Assert.Contains("timed out", second.Message);
            Assert.Equal(1, Volatile.Read(ref starts));
            release.Set();
            var later = Record.Exception(() => bootstrap.Ensure(Start, TimeSpan.FromSeconds(5)));
            if (fail) Assert.Same(failure, Assert.IsType<InvalidOperationException>(later).InnerException);
            else Assert.Null(later);
            Assert.Equal(1, Volatile.Read(ref starts));
        }
        finally { release.Set(); }
    }
}
