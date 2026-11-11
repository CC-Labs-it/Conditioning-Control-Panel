using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MindWipeLoopPreparationTests
{
    [Fact]
    public void Next_voice_is_built_before_overlap_and_started_at_overlap()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var voices = new ConcurrentQueue<Voice>(); var count = 0; var clock = new Clock();
        using var loop = new LibVlcAudio.LoopVoice((started, ended) => {
            if (Interlocked.Increment(ref count) == 2) { entered.Set(); release.Wait(); }
            var v = new Voice(started); voices.Enqueue(v); return v;
        }, () => { }, clock);
        try {
            Assert.True(loop.Play()); Assert.True(entered.Wait(5000));
            release.Set(); Assert.True(SpinWait.SpinUntil(() => voices.Count >= 2, 5000));
            var copies = voices.ToArray(); Assert.Equal(0, copies[1].Plays);
            clock.Fire(); Assert.True(SpinWait.SpinUntil(() => copies[1].Plays == 1, 5000));
        } finally { release.Set(); }
    }

    [Fact]
    public void Stop_during_preparation_disposes_late_voice_without_playing()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var voices = new ConcurrentQueue<Voice>(); var count = 0; var clock = new Clock();
        using var loop = new LibVlcAudio.LoopVoice((started, ended) => {
            if (Interlocked.Increment(ref count) == 2) { entered.Set(); release.Wait(); }
            var v = new Voice(started); voices.Enqueue(v); return v;
        }, () => { }, clock);
        try {
            Assert.True(loop.Play()); Assert.True(entered.Wait(5000)); loop.Dispose(); release.Set();
            Assert.True(SpinWait.SpinUntil(() => voices.Count == 2 && voices.ToArray()[1].Disposed, 5000));
            Assert.Equal(0, voices.ToArray()[1].Plays);
        } finally { release.Set(); }
    }

    [Fact]
    public void Failed_first_build_is_retried_and_starts()
    {
        using var failed = new ManualResetEventSlim(); var count = 0; var clock = new Clock();
        var voices = new ConcurrentQueue<Voice>();
        using var loop = new LibVlcAudio.LoopVoice((started, ended) => {
            if (Interlocked.Increment(ref count) == 1) { failed.Set(); throw new InvalidOperationException(); }
            var v = new Voice(started); voices.Enqueue(v); return v;
        }, () => { }, clock);
        Assert.True(loop.Play()); Assert.True(failed.Wait(5000));
        Assert.True(SpinWait.SpinUntil(() => { clock.Fire(); return voices.Count >= 1; }, 5000));
        Assert.True(SpinWait.SpinUntil(() => voices.ToArray()[0].Plays == 1, 5000));
    }

    [Fact]
    public void Failed_preparation_is_retried_at_overlap()
    {
        using var failed = new ManualResetEventSlim(); var count = 0; var clock = new Clock();
        var voices = new ConcurrentQueue<Voice>();
        using var loop = new LibVlcAudio.LoopVoice((started, ended) => {
            if (Interlocked.Increment(ref count) == 2) { failed.Set(); throw new InvalidOperationException(); }
            var v = new Voice(started); voices.Enqueue(v); return v;
        }, () => { }, clock);
        Assert.True(loop.Play()); Assert.True(failed.Wait(5000));
        Assert.True(SpinWait.SpinUntil(() => { clock.Fire(); return voices.Count >= 2; }, 5000));
        Assert.True(SpinWait.SpinUntil(() => voices.ToArray()[1].Plays == 1, 5000));
    }

    private sealed class Voice(Action<long> started) : MindWipePlayer.IVoice
    {
        public int Plays; public volatile bool Disposed;
        public double Volume { set { } }
        public bool Play() { Interlocked.Increment(ref Plays); started(6000); return true; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Clock : TimeProvider
    {
        private volatile TimerCallback? _callback;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _callback = callback; return new FakeTimer(); }
        public void Fire() => _callback?.Invoke(null);
        private sealed class FakeTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
