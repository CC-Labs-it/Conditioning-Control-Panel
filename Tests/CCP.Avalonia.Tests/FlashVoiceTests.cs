using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashVoiceTests
{
    [Fact]
    public void MutingAnyVoiceControlSuppressesPlayback()
    {
        var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 };
        Assert.True(FlashVoice.Allowed(s));
        s.FlashAudioEnabled = false; Assert.False(FlashVoice.Allowed(s)); s.FlashAudioEnabled = true;
        s.MasterVolume = 0; Assert.False(FlashVoice.Allowed(s)); s.MasterVolume = 100;
        s.AvatarMuted = true; Assert.False(FlashVoice.Allowed(s)); s.AvatarMuted = false;
        s.CompanionVoiceLinesMuted = true; Assert.False(FlashVoice.Allowed(s));
    }

    [Fact]
    public void DisabledAndRemovedPhraseIdsAreExcludedFromEveryFolder()
    {
        var s = new AppSettings();
        s.DisabledPhraseIds.Add("VoiceLine:disabled");
        s.RemovedPhraseIds.Add("VoiceLine:removed");
        Assert.Equal(new[] { "ok.mp3" }, FlashVoice.Filter(s,
            new[] { "ok.mp3", "disabled.wav", "sub/disabled.ogg", "removed.mp3", "ok.mp3" }));
    }

    /// <summary>Fake audio: records plays, ducks, unducks and deferred steps; hands back each clip's callbacks.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly Func<string, float, string, Action<TimeSpan>?, Action?, Action?>? _play = CoreAudio.PlayStoppableProvider;
        private readonly Action<int>? _duck = CoreAudio.DuckProvider;
        private readonly Action<long>? _unduck = CoreAudio.UnduckProvider;
        private readonly Func<long>? _gen = CoreAudio.DuckGenerationProvider;
        private readonly Action<int, Action> _later = FlashVoice.Later;
        internal readonly List<string> Played = new();
        internal readonly List<float> Volumes = new();
        internal readonly List<(Action<TimeSpan>? Started, Action? Finished)> Clips = new();
        internal readonly List<int> Ducks = new();
        internal readonly List<long> Unducks = new();
        internal readonly List<(int Ms, Action Run)> Later = new();
        internal readonly List<double?> Ready = new();
        internal int Stops;

        internal Rig()
        {
            FlashVoice.Bag.Reset();
            CoreAudio.PlayStoppableProvider = (path, volume, _, started, finished) =>
            {
                Played.Add(path); Volumes.Add(volume); Clips.Add((started, finished));
                return () => { Stops++; finished?.Invoke(); };
            };
            CoreAudio.DuckProvider = Ducks.Add;
            CoreAudio.UnduckProvider = Unducks.Add;
            CoreAudio.DuckGenerationProvider = () => 7;
            FlashVoice.Later = (ms, a) => Later.Add((ms, a));
        }

        internal void Play(AppSettings s, params string[] files) => FlashVoice.Play(s, files, Ready.Add);

        internal void RunLater(int ms) { foreach (var l in Later.FindAll(l => l.Ms == ms)) l.Run(); }

        public void Dispose()
        {
            FlashVoice.Stop();
            FlashVoice.Bag.Reset();
            CoreAudio.PlayStoppableProvider = _play; CoreAudio.DuckProvider = _duck;
            CoreAudio.UnduckProvider = _unduck; CoreAudio.DuckGenerationProvider = _gen;
            FlashVoice.Later = _later;
        }
    }

    private static AppSettings Voiced(bool duck = true) =>
        new() { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = duck, DuckingLevel = 70 };

    [Fact]
    public void BurstStartsOneLineAndStopReleasesItOnce()
    {
        using var rig = new Rig();
        rig.Play(Voiced(), "one.mp3", "two.mp3");
        Assert.Single(rig.Played);
        FlashVoice.Stop();
        FlashVoice.Stop();
        Assert.Equal(1, rig.Stops);
    }

    [Fact]
    public void VoiceLevelZeroStillRunsTheClipSilentlyForItsLength()
    {
        using var rig = new Rig();
        var s = Voiced(false);
        s.CompanionVoiceLevel = 0;
        rig.Play(s, "a.mp3");
        Assert.Single(rig.Played);
        Assert.True(rig.Volumes[0] > 0);   // LibVLC refuses a zero volume as a failed clip
        Assert.Equal(0, LibVlcAudio.VlcVolume(rig.Volumes[0]));
        rig.Clips[0].Started!(TimeSpan.FromSeconds(3));
        Assert.Equal(new double?[] { 3.0 }, rig.Ready);
    }

    [Fact]
    public void ThrowingPlaybackProviderFallsBackAndReleasesDucking()
    {
        using var rig = new Rig();
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => throw new InvalidOperationException("player failure");
        rig.Play(Voiced(), "bad.mp3");
        Assert.Equal(new double?[] { 5 }, rig.Ready);
        rig.RunLater(FlashVoice.StartTimeoutMs);
        Assert.Equal(new double?[] { 5 }, rig.Ready);
        rig.RunLater(6500);
        Assert.Equal(new long[] { 7 }, rig.Unducks);
    }

    [Fact]
    public async Task ZeroVoiceLevelProducesNoSamplesFromRealPlayback()
    {
        var (voice, control) = await RecordVoice(0);
        Assert.Empty(voice);
        Assert.Contains(control, v => v != 0);   // the recording output can detect this same tone
    }

    [Fact]
    public async Task LowVoiceLevelIsQuietFromTheFirstRecordedSample()
    {
        // Level 4 of 100 at full master: 0.85 x 0.05, about 680 of the tone's 16000 peak.
        var (voice, control) = await RecordVoice(4);
        Assert.True(voice.Length > 4000, "the low voice recorded nothing");
        Assert.InRange(voice.Take(4410).Max(v => Math.Abs((int)v)), 300, 900);   // first 100 ms included
        Assert.InRange(voice.Max(v => Math.Abs((int)v)), 300, 900);
        Assert.True(control.Max(v => Math.Abs((int)v)) > 15000, "the control was not at full scale");
    }

    [Fact]
    public async Task RecordingKeepsSharedInstanceUnchanged()
    {
        var shared = typeof(LibVlcAudio).GetProperty("Shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = shared.GetValue(null);
        LibVlcAudio? existing = null;
        try
        {
            shared.SetValue(null, null);
            await RecordVoice(4);
            Assert.Null(LibVlcAudio.Shared);
            existing = new LibVlcAudio(false, "--aout=dummy");
            shared.SetValue(null, existing.Vlc);
            await RecordVoice(0);
            Assert.Same(existing.Vlc, LibVlcAudio.Shared);
        }
        finally { shared.SetValue(null, previous); existing?.DisposeUnshared(); }
    }

    /// <summary>A two-second tone through FlashVoice and real LibVLC into a recording output, then the same
    /// tone at full gain as a control: the samples of each.</summary>
    private static async Task<(short[] Voice, short[] Control)> RecordVoice(int level)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("The recording output check runs on Linux.");
        var root = Path.Combine(Path.GetTempPath(), "ccp-flash-gain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wav = Path.Combine(root, "tone.wav");
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldLater = FlashVoice.Later;
        static short[] Samples(string f) => !File.Exists(f) ? Array.Empty<short>()
            : File.ReadAllBytes(f).Skip(44).Chunk(2).Where(b => b.Length == 2).Select(b => BitConverter.ToInt16(b)).ToArray();
        try
        {
            // A non-silent two-second tone exposes even a short leak at player startup.
            const int rate = 22050, count = rate * 2;
            using (var w = new BinaryWriter(File.Create(wav)))
            {
                w.Write("RIFF"u8.ToArray()); w.Write(36 + count * 2); w.Write("WAVEfmt "u8.ToArray());
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
                w.Write((short)2); w.Write((short)16); w.Write("data"u8.ToArray()); w.Write(count * 2);
                for (var n = 0; n < count; n++) w.Write((short)(16000 * Math.Sin(2 * Math.PI * 440 * n / rate)));
            }
            async Task<short[]> Record(string name, Func<LibVlcAudio, Action, Task> play)
            {
                var output = Path.Combine(root, name);
                LibVlcAudio audio;
                try { audio = new LibVlcAudio(false, "--aout=file", "--audiofile-file=" + output, "--audiofile-format=s16"); }
                catch (Exception ex) { Assert.Skip("LibVLC unavailable: " + ex.Message); throw; }
                try
                {
                    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await play(audio, () => ended.TrySetResult());
                    await ended.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    return Samples(output);
                }
                finally
                {
                    FlashVoice.Stop();
                    var gain = typeof(LibVlcAudio).GetField("_gainVlc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(audio);
                    audio.DisposeUnshared();
                    var disposed = audio.Vlc.GetType().BaseType!.GetField("IsDisposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
                    Assert.True((bool)disposed.GetValue(audio.Vlc)!);
                    if (gain != null) Assert.True((bool)disposed.GetValue(gain)!);
                }
            }
            var voice = await Record("voice.wav", async (audio, end) =>
            {
                CoreAudio.PlayStoppableProvider = (path, gain, tag, started, finished) => audio.PlayOneShot(path, gain, tag, started,
                    () => { finished?.Invoke(); end(); });
                FlashVoice.Later = (_, _) => { };
                var ready = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var settings = Voiced(false); settings.CompanionVoiceLevel = level;
                FlashVoice.Play(settings, new[] { wav }, value => ready.TrySetResult(value));
                Assert.Equal(2.0, (await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)))!.Value, 1);
            });
            var control = await Record("control.wav", (audio, end) => { audio.PlayOneShot(wav, 1f, "control", null, end); return Task.CompletedTask; });
            return (voice, control);
        }
        finally
        {
            FlashVoice.Stop();
            CoreAudio.PlayStoppableProvider = oldPlay;
            FlashVoice.Later = oldLater;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NoLineLeavesThePreviousClipPlaying()
    {
        using var rig = new Rig();
        rig.Play(Voiced(false), "a.mp3");
        rig.Play(new AppSettings { FlashAudioEnabled = false, MasterVolume = 100 }, "a.mp3");
        Assert.Equal(0, rig.Stops);
    }

    [Fact]
    public void EveryEligibleLinePlaysBeforeAnyRepeats()
    {
        using var rig = new Rig();
        var pool = new[] { "a.mp3", "b.mp3", "c.mp3", "d.mp3" };
        for (var round = 0; round < 5; round++)
        {
            var start = rig.Played.Count;
            foreach (var _ in pool) rig.Play(Voiced(false), pool);
            Assert.Equal(pool, rig.Played.GetRange(start, pool.Length).Order());
        }
    }

    [Fact]
    public void PlaybackDucksAtTheLevelAndReleasesAfterTheClipPlusOneAndAHalfSeconds()
    {
        using var rig = new Rig();
        rig.Play(Voiced(), "a.mp3");
        Assert.Equal(new[] { 70 }, rig.Ducks);
        rig.Clips[0].Started!(TimeSpan.FromSeconds(3));
        rig.Clips[0].Finished!();   // the clip's natural end settles nothing twice
        Assert.Equal(new double?[] { 3.0 }, rig.Ready);
        Assert.Empty(rig.Unducks);
        rig.RunLater(4500);
        Assert.Equal(new long[] { 7 }, rig.Unducks);
        rig.RunLater(FlashVoice.StartTimeoutMs);   // a late timeout changes nothing
        Assert.Equal(new double?[] { 3.0 }, rig.Ready);
        Assert.Single(rig.Unducks);
    }

    [Fact]
    public void FailureStopAndTimeoutStillReleaseTheDuck()
    {
        using var rig = new Rig();
        rig.Play(Voiced(), "fails.mp3");
        rig.Clips[0].Finished!();   // error before it started
        Assert.Equal(new double?[] { 5 }, rig.Ready);
        rig.Play(Voiced(), "stopped.mp3");
        FlashVoice.Stop();          // panic before it started
        Assert.Equal(new double?[] { 5, 5 }, rig.Ready);
        rig.Play(Voiced(), "silent.mp3");
        rig.RunLater(FlashVoice.StartTimeoutMs);   // never started nor failed
        Assert.Equal(new double?[] { 5, 5, 5 }, rig.Ready);
        rig.RunLater(6500);
        Assert.Equal(new long[] { 7, 7, 7 }, rig.Unducks);
    }

    [Fact]
    public void AClipThatMissesTheStartTimeoutIsStoppedSoItCannotOutliveItsFiveSeconds()
    {
        using var rig = new Rig();
        rig.Play(Voiced(), "slow.mp3");
        rig.RunLater(FlashVoice.StartTimeoutMs);
        Assert.Equal(1, rig.Stops);
        rig.Clips[0].Started!(TimeSpan.FromSeconds(30));   // a late start changes nothing
        Assert.Equal(new double?[] { 5 }, rig.Ready);

        rig.Play(Voiced(), "quick.mp3");                    // stops the first clip's handle again
        rig.Clips[1].Started!(TimeSpan.FromSeconds(3));
        var stops = rig.Stops;
        rig.RunLater(FlashVoice.StartTimeoutMs);             // a started clip is left playing
        Assert.Equal(stops, rig.Stops);
    }

    [Fact]
    public void DuckingOffOrNoLineNeverDucks()
    {
        using var rig = new Rig();
        rig.Play(Voiced(duck: false), "a.mp3");
        rig.Clips[0].Started!(TimeSpan.FromSeconds(2));
        rig.Play(Voiced(), Array.Empty<string>());
        rig.Play(new AppSettings { FlashAudioEnabled = false, MasterVolume = 100, AudioDuckingEnabled = true }, "a.mp3");
        Assert.Empty(rig.Ducks);
        Assert.DoesNotContain(rig.Later, l => l.Ms != FlashVoice.StartTimeoutMs);
        Assert.Equal(new double?[] { 2, null, null }, rig.Ready);
    }

    [Fact]
    public Task PicturesWaitForTheLineAndLiveItsLengthPlusOneSecond() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var present = FlashOverlay.Present;
        var speak = FlashVoice.Speak;
        var addXp = CoreProgression.AddXPProvider;
        var skills = CoreSettings.Current.UnlockedSkills.ToList();
        var xp = new List<double>();
        Action<double?>? ready = null;
        try
        {
            FlashOverlay.Present = (_, _, _) => true;
            FlashVoice.Speak = (_, r) => ready = r;
            CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
            CoreSettings.Current.UnlockedSkills.Clear();   // no lucky x10 roll
            var screen = new Window().Screens.All[0];
            List<(Bitmap, PixelRect, string, Screen)> Burst() => new()
                { (new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))), new PixelRect(100, 100, 100, 100), "a.png", screen) };
            var s = new AppSettings { FlashDuration = 9 };
            var done = new TaskCompletionSource();

            FlashOverlay.SpeakThenSchedule(Burst(), FlashOverlay.Generation, s, false, null, 40, 1, TimeSpan.Zero, false, () => done.SetResult());
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(FlashOverlay.Active);   // the picture shows with the voice, not before it
            ready!(3.0);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(FlashOverlay.Active).Window.Lifetime);
            Assert.Equal(new[] { 8.0 }, xp);   // WPF: 8 XP while the burst's line plays

            // Stop while the line is starting: its late settle spawns nothing.
            FlashOverlay.CloseAll(false);
            ready = null;
            var stale = false;
            FlashOverlay.SpeakThenSchedule(Burst(), FlashOverlay.Generation, s, false, null, 40, 1, TimeSpan.Zero, false, () => stale = true);
            FlashOverlay.CloseAll(false);
            ready!(FlashVoice.FailedSeconds);
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(FlashOverlay.Active);
            Assert.False(stale);

            // No line: the settings duration holds.
            done = new TaskCompletionSource();
            FlashOverlay.SpeakThenSchedule(Burst(), FlashOverlay.Generation, s, false, null, 40, 1, TimeSpan.Zero, false, () => done.SetResult());
            ready!(null);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(FlashOverlay.Active).Window.Lifetime);
            Assert.Equal(new[] { 8.0, 4.0 }, xp);
        }
        finally
        {
            FlashOverlay.CloseAll(false); FlashOverlay.Present = present; FlashVoice.Speak = speak;
            CoreProgression.AddXPProvider = addXp;
            CoreSettings.Current.UnlockedSkills.Clear(); CoreSettings.Current.UnlockedSkills.AddRange(skills);
        }
    });
}
