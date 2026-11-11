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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void First_play_failure_retries_even_after_late_completion(bool throws)
    {
        var clock = new Clock(); var count = 0; var ended = 0;
        FailedVoice? first = null; Voice? next = null;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            var turn = Interlocked.Increment(ref count);
            if (turn == 1) return first = new FailedVoice(started, completed, throws);
            var voice = new Voice(started, completed);
            if (turn == 2) next = voice;
            return voice;
        }, () => Interlocked.Increment(ref ended), clock);
        Assert.True(loop.Play());
        Assert.True(SpinWait.SpinUntil(() => first?.Disposed == true, 5000));
        first!.Complete(); // The failed voice may report completion after Play returned.
        Assert.Equal(0, Volatile.Read(ref ended));
        Assert.True(SpinWait.SpinUntil(() => clock.HasTimer, 5000));
        Assert.Equal(1, Volatile.Read(ref count));
        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => next?.Plays == 1, 5000));
        first.Complete(); // A duplicate late callback must not end the replacement.
        Assert.Equal(0, Volatile.Read(ref ended));
    }

    [Fact]
    public void Late_started_event_from_failed_voice_does_not_replace_retry()
    {
        var clock = new Clock(); var count = 0; FailedVoice? first = null; Voice? next = null;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            var turn = Interlocked.Increment(ref count);
            if (turn == 1) return first = new FailedVoice(started, completed, false);
            var voice = new Voice(started, completed);
            if (turn == 2) next = voice;
            return voice;
        }, () => { }, clock);
        Assert.True(loop.Play());
        Assert.True(SpinWait.SpinUntil(() => first?.Disposed == true && clock.HasTimer, 5000));
        Assert.Equal(100, clock.LastDueMs);
        first!.RaiseStarted(6000);
        Assert.Equal(100, clock.LastDueMs);
        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => next?.Plays == 1, 5000));
    }

    [Fact]
    public void Playback_error_retries_the_prepared_voice()
    {
        var clock = new Clock(); var voices = new ConcurrentQueue<Voice>(); var ended = 0;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            var v = new Voice(started, completed); voices.Enqueue(v); return v;
        }, () => Interlocked.Increment(ref ended), clock);
        Assert.True(loop.Play());
        Assert.True(SpinWait.SpinUntil(() => voices.Count >= 2 && voices.ToArray()[0].Plays == 1, 5000));
        var copies = voices.ToArray();
        copies[0].Complete(true);
        Assert.Equal(0, Volatile.Read(ref ended));
        Assert.Equal(0, copies[1].Plays);
        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => copies[1].Plays == 1, 5000));
    }

    [Fact]
    public void Playback_error_during_preparation_waits_for_retry()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var clock = new Clock(); var count = 0; var ended = 0; Voice? first = null, next = null;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            var turn = Interlocked.Increment(ref count);
            if (turn == 2) { entered.Set(); release.Wait(); }
            var voice = new Voice(started, completed);
            if (turn == 1) first = voice;
            if (turn == 2) next = voice;
            return voice;
        }, () => Interlocked.Increment(ref ended), clock);
        try
        {
            Assert.True(loop.Play()); Assert.True(entered.Wait(5000));
            first!.Complete(true);
            Assert.Equal(0, Volatile.Read(ref ended));
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => next?.VolumeSet == true, 5000));
            loop.Volume = 0.5; // Wait until the preparing worker releases the loop lock.
            Assert.Equal(0, next!.Plays);
            clock.Fire();
            Assert.True(SpinWait.SpinUntil(() => next.Plays == 1, 5000));
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Retiring_previous_voice_does_not_bypass_pending_retry()
    {
        var clock = new Clock(); var count = 0; var ended = 0;
        Voice? first = null, replacement = null; FailedVoice? failed = null;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            var turn = Interlocked.Increment(ref count);
            if (turn == 2) return failed = new FailedVoice(started, completed, false);
            var voice = new Voice(started, completed);
            if (turn == 1) first = voice;
            if (turn == 3) replacement = voice;
            return voice;
        }, () => Interlocked.Increment(ref ended), clock);
        Assert.True(loop.Play());
        Assert.True(SpinWait.SpinUntil(() => first?.Plays == 1 && failed != null, 5000));
        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => failed!.Disposed, 5000));
        first!.Complete(false);
        failed!.Complete();
        Assert.Equal(0, Volatile.Read(ref ended));
        Assert.Equal(2, Volatile.Read(ref count));
        clock.Fire();
        Assert.True(SpinWait.SpinUntil(() => replacement?.Plays == 1, 5000));
    }

    [Fact]
    public void Stop_while_retry_is_pending_prevents_another_build()
    {
        var clock = new Clock(); var count = 0; var ended = 0;
        using var unexpected = new ManualResetEventSlim();
        FailedVoice? first = null;
        using var loop = new LibVlcAudio.LoopVoice((started, completed) => {
            if (Interlocked.Increment(ref count) == 1) return first = new FailedVoice(started, completed, false);
            unexpected.Set(); return new Voice(started, completed);
        }, () => Interlocked.Increment(ref ended), clock);
        Assert.True(loop.Play());
        Assert.True(SpinWait.SpinUntil(() => first?.Disposed == true && clock.HasTimer, 5000));
        loop.Dispose();
        first!.Complete();
        clock.Fire(); // A queued timer may still fire after disposal.
        Assert.False(unexpected.Wait(300));
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.Equal(1, Volatile.Read(ref ended));
    }

    private sealed class FailedVoice(Action<long> started, Action<bool> completed, bool throws) : MindWipePlayer.IVoice
    {
        public volatile bool Disposed;
        public double Volume { set { } }
        public bool Play() { if (throws) throw new InvalidOperationException(); return false; }
        public void Dispose() => Disposed = true;
        public void Complete() => completed(true);
        public void RaiseStarted(long length) => started(length);
    }

    private sealed class Voice(Action<long> started, Action<bool>? completed = null) : MindWipePlayer.IVoice
    {
        public int Plays; public volatile bool Disposed, VolumeSet;
        public double Volume { set => VolumeSet = true; }
        public bool Play() { Interlocked.Increment(ref Plays); started(6000); return true; }
        public void Dispose() => Disposed = true;
        public void Complete(bool failed) => completed?.Invoke(failed);
    }

    private sealed class Clock : TimeProvider
    {
        private volatile TimerCallback? _callback;
        private long _lastDueMs;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Interlocked.Exchange(ref _lastDueMs, (long)dueTime.TotalMilliseconds); _callback = callback; return new FakeTimer(); }
        public long LastDueMs => Interlocked.Read(ref _lastDueMs);
        public bool HasTimer => _callback != null;
        public void Fire() => _callback?.Invoke(null);
        private sealed class FakeTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
