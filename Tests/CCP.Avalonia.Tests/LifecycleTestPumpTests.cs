using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class LifecycleTestPumpTests
{
    [Fact]
    public Task PumpsUntilCompletionBeyondAFixedPassCount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        var probes = 0;
        var turns = 0;
        await LifecycleTestPump.UntilAsync(() =>
        {
            if (++probes == 5) return true;
            Dispatcher.UIThread.Post(() => turns++, DispatcherPriority.Background);
            return false;
        }, "Completion was not observed.");
        Assert.Equal(5, probes);
        Assert.Equal(4, turns);
    });

    [Fact]
    public Task CompletionWinsOverTheHangGuard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        await LifecycleTestPump.UntilAsync(() => true, "Already complete.", timeout: TimeSpan.Zero);
    });

    [Fact]
    public Task ARetainedObjectFailsWithTheCallerDiagnostic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        var retained = new object();
        var weak = new WeakReference(retained);
        var error = await Assert.ThrowsAsync<Xunit.Sdk.TrueException>(() =>
            LifecycleTestPump.UntilAsync(() => !weak.IsAlive, "Still rooted.", collect: true,
                timeout: TimeSpan.Zero));
        Assert.Contains("Still rooted.", error.Message);
        GC.KeepAlive(retained);
    });
}
