using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using App = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;
public class BrainDrainAudioTests
{
    internal sealed class Clock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Voice : LibVlcAudio.IPreparedVoice
    {
        public double Gain;
        public bool Disposed;
        public int Plays;
        public bool PlaySucceeds = true;
        public Action? OnPlay;
        public bool Play() { Plays++; OnPlay?.Invoke(); return PlaySucceeds; }
        public double Volume { set => Gain = value; }
        public void Dispose() => Disposed = true;
    }
    [Theory]
    [InlineData("clips")] [InlineData("stop")] [InlineData("race")] [InlineData("media")] [InlineData("presence")]
    public void TestMethodsRestoreTheirStateBeforeReturning(string method)
    {
        using var state = new BrainDrainTestState();
        Func<string> assets = () => Path.GetTempPath();
        Func<ConditioningControlPanel.Services.SettingsService?> provider = () => null;
        CorePaths.EffectiveAssetsProvider = assets; CoreSettings.ServiceProvider = provider;
        CoreSettings.Current.BrainDrainEnabled = false;
        switch (method)
        {
            case "clips": ClipsFinishLiveGainFollowsRestoreAndPanicStops(); break;
            case "stop": StopDuringDecoderCreationDisposesTheLateVoice(); break;
            case "race": StoppedGenerationNeverStartsPlayback(true, true); break;
            case "media": MediaLogRecordsOnlySuccessfulPlayback("success"); break;
            case "presence": new BrainDrainAudioTestsPresence().OnlyStoppingARunningPlayerClearsPresence(); break;
        }
        Assert.Same(assets, CorePaths.EffectiveAssetsProvider);
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.False(CoreSettings.Current.BrainDrainEnabled);
    }
    [Fact]
    public void TriggeredOnlyWhenAWinningRollSelectsAClip()
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-trigger-").FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "braindrain")).FullName;
        var path = Path.Combine(folder, "clip.wav");
        double roll = 1; int triggered = 0;
        var player = new BrainDrainPlayer((_, _) => new Voice(), new Clock(), () => roll, action => action());
        player.Triggered += (_, _) => triggered++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.BrainDrainEnabled = true;
            player.Tick(TimeSpan.FromSeconds(5)); // stopped
            player.Start(); player.Tick(TimeSpan.FromSeconds(5)); // losing roll
            roll = 0; player.Tick(TimeSpan.FromSeconds(5)); // empty library
            Assert.Equal(0, triggered);
            File.WriteAllText(path, "");
            player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, triggered);
            player.Tick(TimeSpan.FromSeconds(5)); // busy
            player.Stop(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, triggered);
        }
        finally { player.Stop(); Directory.Delete(root, true); }
    }
    [Fact]
    public void ClipsFinishLiveGainFollowsRestoreAndPanicStops()
    {
        var state = new BrainDrainTestState();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var service = new ConditioningControlPanel.Services.SettingsService();
        var voice = new Voice(); int plays = 0; Action? ended = null;
        voice.OnPlay = () => plays++;
        var player = new BrainDrainPlayer((_, done) =>
        { ended = done; return voice; }, new Clock(), () => 0);
        var oldInstance = BrainDrainPlayer.Instance;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CorePaths.EffectiveAssetsProvider = () => root;
            service.Current.BrainDrainEnabled = true;
            service.Current.MasterVolume = 50; service.Current.BrainDrainVolume = 40;
            BrainDrainPlayer.Instance = player;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, plays); Assert.Equal(.2, voice.Gain, 8);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(1, plays);
            service.Current.BrainDrainVolume = 80; Assert.Equal(.4, voice.Gain, 8);
            service.RestoreFrom(new ConditioningControlPanel.Models.AppSettings
                { BrainDrainEnabled = true, MasterVolume = 100, BrainDrainVolume = 50 });
            Assert.Equal(.5, voice.Gain, 8);
            service.Current.BrainDrainVolume = 20; Assert.Equal(.2, voice.Gain, 8);
            ended!(); player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
            // Exercise the registered panic surface without creating any windows.
            Assert.Single(PanicSurfaces.All, x => x.Id == "brain-drain-audio").Stop(null);
            Assert.True(voice.Disposed); Assert.False(player.IsRunning);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
        }
        finally { player.Stop(); BrainDrainPlayer.Instance = oldInstance; state.Dispose(); Directory.Delete(root, true); }
    }
    [Fact]
    public void TriggerReachesSubscriberWhilePreparationIsBlocked()
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-blocked-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Action? queued = null; int count = 0; Task? tick = null;
        var player = new BrainDrainPlayer((_, _) =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "preparation was never released");
            return new Voice();
        }, new Clock(), () => 0, action => queued = action);
        player.Triggered += (_, _) => count++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); tick = Task.Run(() => player.Tick(TimeSpan.FromSeconds(5)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "preparation never started");
            Assert.NotNull(queued); queued!();
            Assert.Equal(1, count); Assert.False(tick.IsCompleted);
            release.Set(); tick.GetAwaiter().GetResult();
            Assert.Equal(1, count);
        }
        finally
        {
            release.Set();
            try { tick?.GetAwaiter().GetResult(); }
            finally { player.Stop(); Directory.Delete(root, true); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoppedDuringPreparationDropsTriggerEvenAfterRestart(bool restart)
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-stop-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        Action? queued = null; int count = 0;
        BrainDrainPlayer? player = null;
        player = new BrainDrainPlayer((_, _) => { player!.Stop(); if (restart) player.Start(); return new Voice(); },
            new Clock(), () => 0, action => queued = action);
        player.Triggered += (_, _) => count++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.NotNull(queued); queued!(); Assert.Equal(0, count);
        }
        finally { player.Stop(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedTriggerIsDroppedAfterStopEvenIfRestarted(bool restart)
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-queued-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        Action? queued = null; int count = 0;
        var player = new BrainDrainPlayer((_, _) => new Voice(), new Clock(), () => 0, action => queued = action);
        player.Triggered += (_, _) => count++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5)); Assert.NotNull(queued);
            player.Stop(); if (restart) player.Start(); queued!(); Assert.Equal(0, count);
        }
        finally { player.Stop(); Directory.Delete(root, true); }
    }

    [Fact]
    public void QueuedTriggerSurvivesClipCompletion()
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-ended-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        Action? queued = null, ended = null; int count = 0;
        var player = new BrainDrainPlayer((_, done) => { ended = done; return new Voice(); },
            new Clock(), () => 0, action => queued = action);
        player.Triggered += (_, _) => count++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.NotNull(ended); Assert.NotNull(queued);
            ended!(); queued!(); Assert.Equal(1, count);
        }
        finally { player.Stop(); Directory.Delete(root, true); }
    }

    [Fact]
    public void DecoderExceptionStillRaisesTrigger()
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-throw-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        int count = 0;
        var player = new BrainDrainPlayer((_, _) => throw new IOException("decoder failed"),
            new Clock(), () => 0, action => action());
        player.Triggered += (_, _) => count++;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(1, count);
        }
        finally { player.Stop(); Directory.Delete(root, true); }
    }

    [Fact]
    public void StopDuringDecoderCreationDisposesTheLateVoice()
    {
        var state = new BrainDrainTestState();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var voice = new Voice(); BrainDrainPlayer? player = null;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.BrainDrainEnabled = true;
            player = new BrainDrainPlayer((_, _) => { player!.Stop(); return voice; }, new Clock(), () => 0);
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.True(voice.Disposed); Assert.False(player.IsRunning);
            Assert.Equal(0, voice.Plays);
        }
        finally { player?.Stop(); state.Dispose(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StoppedGenerationNeverStartsPlayback(bool panic, bool restart)
    {
        var state = new BrainDrainTestState();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var voice = new Voice(); int triggers = 0; Action? queued = null;
        var player = new BrainDrainPlayer((_, _) =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "preparation was never released");
            return voice;
        }, new Clock(), () => 0, action => queued = action);
        player.Triggered += (_, _) => triggers++;
        var oldInstance = BrainDrainPlayer.Instance;
        Task? tick = null;
        var history = new ConditioningControlPanel.Services.MediaHistoryService(Path.Combine(root, "history.json"));
        var oldHistory = global::ConditioningControlPanel.Avalonia.App.MediaHistory;
        var features = new System.Collections.Generic.List<string>();
        void OnFeature(string feature) => features.Add(feature);
        App.FeatureUsed += OnFeature;
        try
        {
            global::ConditioningControlPanel.Avalonia.App.MediaHistory = history;
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.BrainDrainEnabled = true;
            BrainDrainPlayer.Instance = player;
            player.Start(); tick = Task.Run(() => player.Tick(TimeSpan.FromSeconds(5)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "decoder preparation never started");
            if (panic) Assert.Single(PanicSurfaces.All, x => x.Id == "brain-drain-audio").Stop(null);
            else player.Stop();
            Assert.False(player.IsRunning);
            if (restart) player.Start();
            Assert.NotNull(queued); queued!();
            release.Set(); tick.GetAwaiter().GetResult();
            Assert.Equal(0, voice.Plays);
            Assert.Equal(0, triggers);
            Assert.True(voice.Disposed);
            Assert.Empty(history.GetSnapshot());
            Assert.Equal(new[] { "braindrain" }, features);
        }
        finally
        {
            App.FeatureUsed -= OnFeature;
            release.Set();
            try { tick?.GetAwaiter().GetResult(); }
            finally
            {
                player.Stop(); BrainDrainPlayer.Instance = oldInstance;
                global::ConditioningControlPanel.Avalonia.App.MediaHistory = oldHistory;
                state.Dispose(); history.Dispose(); Directory.Delete(root, true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoFeatureSignalWithoutASelectedClip(bool noClips)
    {
        using var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-trigger-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        if (!noClips) File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var features = new System.Collections.Generic.List<string>();
        void OnFeature(string feature) => features.Add(feature);
        var player = new BrainDrainPlayer((_, _) => throw new Exception("unexpected preparation"), new Clock(), () => noClips ? 0 : 1);
        App.FeatureUsed += OnFeature;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.BrainDrainEnabled = true;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.Empty(features);
        }
        finally
        {
            App.FeatureUsed -= OnFeature;
            player.Stop(); Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("null")]
    [InlineData("throws")]
    [InlineData("failed-play")]
    [InlineData("suppressed")]
    public void MediaLogRecordsOnlySuccessfulPlayback(string outcome)
    {
        var state = new BrainDrainTestState();
        var root = Directory.CreateTempSubdirectory("ccp-drain-log-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        var path = Path.Combine(root, "braindrain", "clip.wav"); File.WriteAllText(path, "");
        var history = new ConditioningControlPanel.Services.MediaHistoryService(Path.Combine(root, "history.json"));
        var oldHistory = global::ConditioningControlPanel.Avalonia.App.MediaHistory;
        var features = new System.Collections.Generic.List<string>();
        void OnFeature(string feature) => features.Add(feature);
        App.FeatureUsed += OnFeature;
        var oldSuppressed = CoreAudio.IsOutputSuppressedProvider;
        int preparations = 0;
        var voice = new Voice { PlaySucceeds = outcome != "failed-play" };
        var player = new BrainDrainPlayer((_, _) =>
        {
            preparations++;
            return outcome switch { "throws" => throw new IOException("decoder failed"), "null" => null, _ => voice };
        }, new Clock(), () => 0);
        try
        {
            global::ConditioningControlPanel.Avalonia.App.MediaHistory = history;
            CorePaths.EffectiveAssetsProvider = () => root; CoreSettings.Current.BrainDrainEnabled = true;
            CoreAudio.IsOutputSuppressedProvider = () => outcome == "suppressed";
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            if (outcome == "success")
            {
                var entry = Assert.Single(history.GetSnapshot());
                Assert.Equal(path, entry.FilePath);
                Assert.Equal(ConditioningControlPanel.Models.MediaType.Audio, entry.Type);
                player.Tick(TimeSpan.FromSeconds(5)); Assert.Single(history.GetSnapshot());
            }
            else Assert.Empty(history.GetSnapshot());
            Assert.Equal(new[] { "braindrain" }, features);
            if (outcome == "suppressed") Assert.Equal(0, preparations);
        }
        finally
        {
            App.FeatureUsed -= OnFeature;
            CoreAudio.IsOutputSuppressedProvider = oldSuppressed;
            player.Stop(); global::ConditioningControlPanel.Avalonia.App.MediaHistory = oldHistory;
            state.Dispose(); history.Dispose(); Directory.Delete(root, true);
        }
    }
}

// Explicit local cleanup also covers direct calls that do not get a separate assembly hook.
internal sealed class BrainDrainTestState : IDisposable
{
    private readonly Func<string>? _assets = CorePaths.EffectiveAssetsProvider;
    private readonly Func<ConditioningControlPanel.Services.SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly ConditioningControlPanel.Models.AppSettings _settings = CoreSettings.Current;
    private readonly bool _enabled;
    public BrainDrainTestState() => _enabled = _settings.BrainDrainEnabled;
    public void Dispose()
    { CorePaths.EffectiveAssetsProvider = _assets; CoreSettings.ServiceProvider = _provider; _settings.BrainDrainEnabled = _enabled; }
}
