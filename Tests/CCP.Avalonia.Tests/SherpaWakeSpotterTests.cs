using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SherpaWakeSpotterTests
{
    private sealed class Handle : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void MissingModelDoesNotInitialize()
    {
        int calls = 0;
        using var spotter = new SherpaWakeSpotter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), (_, _, _) => { calls++; return new Handle(); });
        Assert.False(spotter.IsAvailable(true, .15, 2));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FingerprintChangesRebuildAndFailureIsMemoizedUntilReset()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" })
                File.WriteAllText(Path.Combine(root, name), "");
            int calls = 0;
            var first = new Handle();
            using var spotter = new SherpaWakeSpotter(root, (_, _, _) =>
            {
                calls++;
                if (calls > 1) throw new InvalidOperationException();
                return first;
            });
            Assert.False(spotter.IsAvailable(false, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.Equal(1, calls);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.True(first.Disposed);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(2, calls);
            spotter.ResetInitState();
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(3, calls);
        }
        finally { Directory.Delete(root, true); }
    }
}

[Collection(RunsAloneCollection.Name)]
public sealed class SherpaWakeCaptureTests
{
    private sealed class Handle : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Mic : ConditioningControlPanel.Services.Speech.IMicSource
    {
        internal Action<byte[], int>? Feed;
        internal int Stops;
        public bool HasDevice => true;
        public System.Collections.Generic.IReadOnlyList<ConditioningControlPanel.SpeechInputDevice> ListDevices() => Array.Empty<ConditioningControlPanel.SpeechInputDevice>();
        public IDisposable Start(Action<byte[], int> feed) { Feed = feed; return new Stop(this); }
        private sealed class Stop(Mic mic) : IDisposable { public void Dispose() { mic.Feed = null; mic.Stops++; } }
    }
    private static string Model()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" }) File.WriteAllText(Path.Combine(root, name), "");
        return root;
    }
    private static async System.Threading.Tasks.Task Until(Func<bool> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < end, "timed out");
            await System.Threading.Tasks.Task.Delay(20);
        }
    }

    [Fact]
    public System.Threading.Tasks.Task WakeLoopUsesSpotterAndKeywordHitHasNoTranscript() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Model();
        var mic = new Mic();
        var engineHandle = new Handle();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => engineHandle, _ => new Handle(), (_, _, samples) => samples.Length > 0);
        using var speech = new ConditioningControlPanel.Services.Speech.SpeechEngine(mic, Array.Empty<string>());
        using var cancel = new System.Threading.CancellationTokenSource();
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var hit = false;
        string? transcript = "unset";
        shell.WakeSpeechOverride = speech;
        shell.WakeSpeechReadyOverride = () => true;
        shell.WakeSpotterOverride = kws;
        shell.WakeMicOverride = mic;
        shell.WakeVoskOverride = (_, _) => throw new InvalidOperationException("Vosk should not run");
        shell.WakeHeardOverride = text => { transcript = text; hit = true; cancel.Cancel(); };
        var noise = ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression;
        var words = ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords;
        ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = "hey bambi";
        ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression = false;
        try
        {
            var loop = shell.WakeLoopAsync(cancel.Token);
            await Until(() => mic.Feed != null && kws.IsListening);
            kws.ResetInitState();
            Assert.False(engineHandle.Disposed);
            shell.CancelVoicePrompt();
            await Until(() => mic.Stops >= 1 && mic.Feed != null && kws.IsListening);
            mic.Feed!(new byte[] { 1, 0 }, 2);
            await Until(() => hit);
            await loop;
            Assert.Null(transcript);
            Assert.False(kws.IsListening);
            Assert.Equal(2, mic.Stops);
        }
        finally { cancel.Cancel(); shell.RequestExit(); ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression = noise; ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = words; Directory.Delete(root, true); }
    });

    [Fact]
    public System.Threading.Tasks.Task MissingModelUsesVoskFallback() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var mic = new Mic();
        using var kws = new SherpaWakeSpotter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        using var speech = new ConditioningControlPanel.Services.Speech.SpeechEngine(mic, Array.Empty<string>());
        using var cancel = new System.Threading.CancellationTokenSource();
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var called = false;
        var words = ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords;
        ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = "hey bambi";
        shell.WakeSpeechOverride = speech;
        shell.WakeSpeechReadyOverride = () => true;
        shell.WakeSpotterOverride = kws;
        shell.WakeMicOverride = mic;
        shell.WakeVoskOverride = (_, _) => { called = true; cancel.Cancel(); return System.Threading.Tasks.Task.FromResult<string?>(null); };
        try { await shell.WakeLoopAsync(cancel.Token); Assert.True(called); Assert.Null(mic.Feed); }
        finally { cancel.Cancel(); shell.RequestExit(); ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = words; }
    });

    [Fact]
    public async System.Threading.Tasks.Task CancellationClosesCaptureAndDropsListening()
    {
        var root = Model();
        var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(), (_, _, _) => false);
        using var cancel = new System.Threading.CancellationTokenSource();
        try
        {
            var wait = kws.WaitForWakeAsync(mic, .15, 2, cancel.Token);
            Assert.True(kws.IsListening);
            cancel.Cancel();
            Assert.False(await wait);
            Assert.False(kws.IsListening);
            Assert.Null(mic.Feed);
            Assert.Equal(1, mic.Stops);
        }
        finally { Directory.Delete(root, true); }
    }
}
