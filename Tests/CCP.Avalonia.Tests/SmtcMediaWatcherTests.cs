using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SmtcMediaWatcherTests
{
    private sealed class Clock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static Task<(string? Title, string? Artist)?> Props(object _) => Task.FromResult<(string?, string?)?>(("Track", "Artist"));
    private static Task<MediaSample?> Map(Func<object?> session, Func<object, Task<(string? Title, string? Artist)?>>? props = null,
        Func<object, string?>? state = null, Func<object, TimeSpan?>? position = null) =>
        SmtcMediaWatcher.ReadSessionAsync(session, props ?? Props, state ?? (_ => "Playing"), position ?? (_ => TimeSpan.FromSeconds(42)));

    [Fact]
    public async Task AcquiresOnceAndReadsTheManager()
    {
        var manager = new object(); int acquisitions = 0;
        using var watcher = new SmtcMediaWatcher(() => { acquisitions++; return Task.FromResult<object?>(manager); },
            value => { Assert.Same(manager, value); return Map(() => new object()); }, new Clock());
        watcher.Start(); await watcher.StartCompletion;
        Assert.True(watcher.IsAvailable);
        await watcher.PollAsync();
        Assert.Equal(new MediaSample("Track", "Artist", "Playing", TimeSpan.FromSeconds(42)), watcher.Current);
        watcher.Stop(); watcher.Start(); await watcher.StartCompletion;
        Assert.Equal(1, acquisitions);
        await watcher.PollAsync();
        watcher.Dispose();
        Assert.False(watcher.IsAvailable);
        Assert.Null(watcher.Current);
    }

    [Fact]
    public async Task MissingOrDisappearingSessionClearsSample()
    {
        Assert.Null(await Map(() => null));
        Assert.Null(await Map(() => throw new InvalidOperationException()));
        Assert.Null(await Map(() => new object(), _ => throw new InvalidOperationException()));
        Assert.Null(await Map(() => new object(), _ => Task.FromResult<(string?, string?)?>(null)));
        Assert.Null(await Map(() => new object(), _ => Task.FromResult<(string?, string?)?>((" ", "Artist"))));
        bool present = true;
        using var watcher = new SmtcMediaWatcher(() => Task.FromResult<object?>(new object()),
            _ => Map(() => present ? new object() : null), new Clock());
        watcher.Start(); await watcher.StartCompletion; await watcher.PollAsync();
        Assert.NotNull(watcher.Current);
        present = false; await watcher.PollAsync();
        Assert.Null(watcher.Current);
    }

    [Fact]
    public async Task MissingPlaybackAndPositionUseWpfDefaults()
    {
        var sample = await Map(() => new object(), _ => Task.FromResult<(string?, string?)?>(("Track", " ")),
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        Assert.Equal(new MediaSample("Track", null, "Unknown", TimeSpan.Zero), sample);
        Assert.Equal(new MediaSample("Track", "Artist", "Unknown", TimeSpan.Zero),
            await Map(() => new object(), state: _ => null, position: _ => null));
    }

    [Fact]
    public async Task AcquisitionFailureAndDisposeDuringAcquisitionStayUnavailable()
    {
        using var denied = new SmtcMediaWatcher(() => throw new InvalidOperationException(), _ => Map(() => new object()), new Clock());
        denied.Start(); await denied.StartCompletion;
        Assert.False(denied.IsAvailable);
        using var missing = new SmtcMediaWatcher(() => Task.FromResult<object?>(null), _ => Map(() => new object()), new Clock());
        missing.Start(); await missing.StartCompletion;
        Assert.False(missing.IsAvailable);
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new SmtcMediaWatcher(() => pending.Task, _ => Map(() => new object()), new Clock());
        watcher.Start(); var started = watcher.StartCompletion;
        watcher.Dispose(); pending.SetResult(new object()); await started;
        Assert.False(watcher.IsAvailable); Assert.Null(watcher.Current);
        watcher.Start(); await watcher.PollAsync();
        Assert.False(watcher.IsAvailable);
    }

    [Fact]
    public async Task NativeAcquisitionIsSafeOnTheHost()
    {
        using var watcher = new SmtcMediaWatcher();
        watcher.Start();
        await watcher.StartCompletion.WaitAsync(TimeSpan.FromSeconds(15));
        await watcher.PollAsync();
        if (!OperatingSystem.IsWindows()) Assert.False(watcher.IsAvailable);
    }
}
