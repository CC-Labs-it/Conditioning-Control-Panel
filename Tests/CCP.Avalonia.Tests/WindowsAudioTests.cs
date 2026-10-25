using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WindowsAudioTests
{
    private sealed class Stream(int pid, string name, float volume)
    {
        internal float Volume = volume;
        internal bool FailWrite;
        internal WindowsAudio.Session Session => new(pid, name, () => Volume, v =>
        {
            if (FailWrite) throw new InvalidOperationException("endpoint gone");
            Volume = v;
        });
    }

    [Fact]
    public void SweepSkipsOwnSystemAndBrowserSessionsAndRestoresOriginals()
    {
        var streams = new[] { new Stream(0, "system", 1), new Stream(Environment.ProcessId, "app", 1),
            new Stream(12, "msedgewebview2", 1), new Stream(13, "music", .8f) };
        var windows = new WindowsAudio(visit => { foreach (var stream in streams) visit(stream.Session); });
        windows.Sweep(.25, false, true, () => true);
        Assert.Equal(new[] { 1f, 1f, 1f, .2f }, Array.ConvertAll(streams, s => s.Volume));
        windows.Sweep(.25, true, true, () => true);
        Assert.Equal(.2f, streams[3].Volume); // rescan never compounds
        windows.Restore();
        Assert.Equal(.8f, streams[3].Volume);
        windows.Sweep(.5, false, false, () => true);
        Assert.Equal(.5f, streams[2].Volume); // setting off permits WebView audio ducking
        windows.Restore();
    }

    [Fact]
    public void RescanSkipsSilentStreamsAndRetiredEpochs()
    {
        var stream = new Stream(22, "music", .001f);
        var windows = new WindowsAudio(visit => visit(stream.Session));
        windows.Sweep(0, true, false, () => true);
        Assert.Equal(.001f, stream.Volume);
        stream.Volume = .8f;
        windows.Sweep(0, true, false, () => false);
        Assert.Equal(.8f, stream.Volume);
        windows.Sweep(.5, true, false, () => true);
        Assert.Equal(.4f, stream.Volume);
        windows.Restore();
        Assert.Equal(.8f, stream.Volume);
    }

    [Fact]
    public void RestoreMatchesRestartedProcessAndRetainsFailedOriginal()
    {
        var stream = new Stream(31, "music", .8f);
        var windows = new WindowsAudio(visit => visit(stream.Session));
        windows.Sweep(.5, false, false, () => true);
        stream.FailWrite = true;
        windows.Restore();
        stream.FailWrite = false;
        windows.Sweep(.5, false, false, () => true);
        Assert.Equal(.4f, stream.Volume);
        stream = new Stream(32, "MUSIC", .1f);
        windows.Restore();
        Assert.Equal(.8f, stream.Volume);
    }

    [Fact]
    public void WindowsBackendUsesRefCountsGenerationAndShutdownRestore()
    {
        var oldMaster = CoreSettings.Current.MasterVolume;
        CoreSettings.Current.MasterVolume = 50;
        var stream = new Stream(41, "music", .6f);
        var audio = new LibVlcAudio(new WindowsAudio(visit => visit(stream.Session)));
        try
        {
            audio.Duck(50); audio.Duck(50); audio.Drain();
            Assert.Equal(.3f, stream.Volume);
            audio.Unduck(-1); audio.Drain();
            Assert.Equal(.3f, stream.Volume);
            audio.ForceUnduck(); audio.Drain();
            Assert.Equal(.6f, stream.Volume);
            audio.Duck(50); audio.Unduck(0); audio.Drain();
            Assert.True(audio.IsDucked);
            audio.Shutdown();
            Assert.Equal(.6f, stream.Volume);
        }
        finally { audio.Shutdown(); CoreSettings.Current.MasterVolume = oldMaster; }
    }

    [Fact]
    public void CrashRecoveryPersistsOriginalAndSkipsOwnAndSystemSessions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-audio-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "recovery.json");
        var stream = new Stream(61, "music", .8f);
        try
        {
            new WindowsAudio(visit => visit(stream.Session), path).Sweep(.5, false, false, () => true);
            Assert.Equal(.4f, stream.Volume);
            var stored = JsonSerializer.Deserialize<Dictionary<int, float>>(File.ReadAllText(path))!;
            Assert.Equal(.8f, stored[61]);
            stored[0] = .2f; stored[Environment.ProcessId] = .2f;
            File.WriteAllText(path, JsonSerializer.Serialize(stored));
            var own = new Stream(Environment.ProcessId, "app", 1);
            var system = new Stream(0, "system", 1);
            new WindowsAudio(visit => { visit(stream.Session); visit(own.Session); visit(system.Session); }, path).Recover();
            Assert.Equal(.8f, stream.Volume);
            Assert.Equal(1, own.Volume); Assert.Equal(1, system.Volume);
            Assert.False(File.Exists(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void DeferredRestoreRetriesAtFiveSecondsAndExpiresAtThreeMinutes()
    {
        var clock = new Clock();
        var stream = new Stream(71, "music", .8f);
        var audio = new LibVlcAudio(new WindowsAudio(visit => visit(stream.Session)), clock);
        var oldMaster = CoreSettings.Current.MasterVolume;
        CoreSettings.Current.MasterVolume = 50;
        try
        {
            audio.Duck(50); audio.Drain();
            stream.FailWrite = true;
            audio.Unduck(-1); audio.Drain();
            Assert.Equal(TimeSpan.FromSeconds(5), clock.Period);
            stream.FailWrite = false;
            clock.Tick(TimeSpan.FromSeconds(5)); audio.Drain();
            Assert.Equal(.8f, stream.Volume);
            clock.Tick(TimeSpan.FromSeconds(5));
            Assert.True(clock.Timer!.Disposed);
            audio.Duck(50); audio.Drain();
            stream.FailWrite = true;
            audio.Unduck(-1); audio.Drain();
            clock.Tick(TimeSpan.FromMinutes(3)); audio.Drain();
            Assert.True(clock.Timer!.Disposed);
            Assert.Equal(.4f, stream.Volume);
        }
        finally { stream.FailWrite = false; audio.Shutdown(); CoreSettings.Current.MasterVolume = oldMaster; }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        internal FakeTimer? Timer;
        internal TimeSpan Period;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
        { Period = period; return Timer = new FakeTimer(() => callback(state)); }
        internal void Tick(TimeSpan elapsed) { _now += elapsed; if (Timer is { Disposed: false }) Timer.Fire(); }
        internal sealed class FakeTimer(Action fire) : ITimer
        {
            internal bool Disposed;
            internal void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    [Fact]
    public void FailedEndpointEnumerationRetainsOriginalForRetry()
    {
        var fail = false;
        var stream = new Stream(81, "music", .8f);
        var windows = new WindowsAudio(visit =>
        {
            if (fail) throw new InvalidOperationException("endpoint unavailable");
            visit(stream.Session);
        });
        windows.Sweep(.5, false, false, () => true);
        fail = true;
        windows.Restore();
        Assert.True(windows.HasPendingRestores);
        fail = false;
        windows.Restore();
        Assert.Equal(.8f, stream.Volume);
        Assert.False(windows.HasPendingRestores);
    }

    [Fact]
    public void EndpointEnumerationPreservesDefaultOrderAndPartialResults()
    {
        var devices = new List<LibVlcAudio.OutputDevice> { new("", "System default") };
        LibVlcAudio.AppendDevices(devices, add =>
        {
            add(new("endpoint-1", "Speakers"));
            add(new("endpoint-2", "Headset"));
            throw new InvalidOperationException("device removed");
        });
        Assert.Equal(new[] { "", "endpoint-1", "endpoint-2" }, devices.ConvertAll(d => d.Id));
        Assert.Equal("endpoint-2", LibVlcAudio.PreferredDevice("endpoint-2", "endpoint-1", devices.ConvertAll(d => d.Id)));
        Assert.Null(LibVlcAudio.PreferredDevice("unplugged", "", devices.ConvertAll(d => d.Id)));
    }
}
