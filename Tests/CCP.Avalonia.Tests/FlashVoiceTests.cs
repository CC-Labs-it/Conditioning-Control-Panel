using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Bark;
using System.Reflection;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FlashVoiceTests
{
    private sealed class VoiceClock : TimeProvider
    {
        public long Ticks;
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    [Fact]
    public void Audio_length_controls_the_burst_lifetime_and_stay_still_wins()
    {
        var s = new AppSettings { FlashDuration = 5, FlashClickable = true, FlashStayUntilPopped = true };
        Assert.Equal(TimeSpan.FromSeconds(4), FlashOverlay.VoiceLifetime(s, false, 2000, 3));
        Assert.Equal(TimeSpan.FromSeconds(3), FlashOverlay.VoiceLifetime(s, false, 2000, null));
        Assert.Equal(TimeSpan.FromMinutes(10), FlashOverlay.VoiceLifetime(s, true, 2000, 3));
    }

    [Fact]
    public Task Flash_text_shows_silently_without_interrupting_a_bubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var muted = s.AvatarMuted;
        s.AvatarMuted = false;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.ShowFlashVoice("first clip");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("first clip", text.Text);
            tube.ShowFlashVoice("second clip");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("first clip", text.Text);
        }
        finally { tube.Close(); s.AvatarMuted = muted; }
        return Task.CompletedTask;
    });

    [Fact]
    public void Voice_pool_honors_the_active_mod_and_disabled_or_removed_lines()
    {
        var root = Path.Combine(Path.GetTempPath(), "flash-voice-" + Guid.NewGuid());
        var folder = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        var oldMod = CoreMods.ActiveModPackageProvider;
        var oldId = CoreMods.ActiveModIdProvider;
        try
        {
            foreach (var file in new[] { "one.mp3", "off.wav", "removed.ogg", "nested/one.mp3", "nested/two.wav" })
                File.WriteAllText(Path.Combine(folder, file), "test");
            CoreMods.ActiveModIdProvider = () => "custom";
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest(), root, false);
            var s = new AppSettings();
            s.DisabledPhraseIds.Add("VoiceLine:off");
            s.RemovedPhraseIds.Add("VoiceLine:removed");
            var pool = FlashOverlay.VoicePool(s);
            Assert.Equal(2, pool.Count);
            Assert.Contains(Path.Combine(folder, "one.mp3"), pool);
            Assert.Contains(Path.Combine(folder, "nested", "two.wav"), pool);
        }
        finally
        {
            CoreMods.ActiveModPackageProvider = oldMod;
            CoreMods.ActiveModIdProvider = oldId;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Voice_obeys_mutes_and_ducks_for_clip_length_with_guarded_release()
    {
        var file = Path.GetTempFileName();
        var oldAfter = FlashOverlay.AfterVoice;
        var oldDuration = FlashOverlay.VoiceDuration;
        FlashOverlay.VoiceDuration = _ => Task.FromResult<double?>(null);
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldDuck = CoreAudio.DuckProvider;
        var oldUnduck = CoreAudio.UnduckProvider;
        var oldGeneration = CoreAudio.DuckGenerationProvider;
        try
        {
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, CompanionVoiceLevel = 80,
                AudioDuckingEnabled = true, DuckingLevel = 72, AvatarMuted = false, CompanionVoiceLinesMuted = false };
            int plays = 0, strength = 0, delay = 0, stops = 0;
            long released = -1;
            float gain = 0;
            Action? release = null;
            CoreAudio.PlayStoppableProvider = (path, vol, tag, start, finish) =>
            {
                plays++; gain = vol;
                Assert.Equal(file, path); Assert.Equal("flash-voice", tag);
                start!(TimeSpan.FromSeconds(3));
                return () => stops++;
            };
            CoreAudio.DuckProvider = n => strength = n;
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = n => released = n;
            FlashOverlay.AfterVoice = (ms, action) => { delay = ms; release = action; };

            Assert.Equal(3, await FlashOverlay.PlayFlashVoice(s, file));
            Assert.Equal(1, plays);
            Assert.Equal((float)Math.Pow(0.5, 1.5) * 0.85f, gain, 5);
            Assert.Equal(72, strength); Assert.Equal(4500, delay);
            release!(); Assert.Equal(42, released);
            FlashOverlay.CloseAll(false); Assert.Equal(1, stops);
            s.FlashAudioEnabled = false;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file));
            s.FlashAudioEnabled = true; s.AvatarMuted = true;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file));
            s.AvatarMuted = false; s.CompanionVoiceLinesMuted = true;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file));
            s.CompanionVoiceLinesMuted = false; s.MasterVolume = 0;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file));
            Assert.Equal(1, plays);
            s.MasterVolume = 50; s.CompanionVoiceLevel = 0;
            Assert.Equal(3, await FlashOverlay.PlayFlashVoice(s, file));
            Assert.Equal(0, gain); Assert.Equal(2, plays);
            Assert.Equal(4500, delay);
            FlashOverlay.CloseAll(false);
        }
        finally
        {
            FlashOverlay.AfterVoice = oldAfter;
            FlashOverlay.VoiceDuration = oldDuration;
            CoreAudio.PlayStoppableProvider = oldPlay;
            CoreAudio.DuckProvider = oldDuck;
            CoreAudio.UnduckProvider = oldUnduck;
            CoreAudio.DuckGenerationProvider = oldGeneration;
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Live_flash_off_retires_a_pending_burst_and_stops_a_playing_voice(int phase)
    {
        var file = Path.GetTempFileName();
        var running = typeof(CoreEngine).GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldRunning = running.GetValue(null);
        var (oldStop, oldPost, oldLead, oldDuration, oldPlay) = (CoreFlash.StopProvider, CoreDispatch.PostProvider,
            FlashOverlay.VoiceLead, FlashOverlay.VoiceDuration, CoreAudio.PlayStoppableProvider);
        var lead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            int plays = 0, stops = 0;
            running.SetValue(null, true);
            CoreDispatch.PostProvider = action => action();
            FlashOverlay.SeedCore(() => null, () => { });
            FlashOverlay.VoiceLead = () => lead.Task;
            FlashOverlay.VoiceDuration = _ => Task.FromResult<double?>(2);
            CoreAudio.PlayStoppableProvider = (_, _, _, start, _) => { plays++; start!(TimeSpan.FromSeconds(2)); return () => stops++; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 };
            if (phase == 0)
            {
                var oldBurst = FlashOverlay.BeginBurst();
                var pending = FlashOverlay.VoiceAfterLead(s, oldBurst, file);
                CoreEngine.ApplyLive("flash", false);
                Assert.False(FlashOverlay.IsBusy);
                var newBurst = FlashOverlay.BeginBurst();
                FlashOverlay.FinishBurst(oldBurst);
                Assert.True(FlashOverlay.IsBusy);
                lead.SetResult();
                Assert.Null(await pending);
                Assert.Equal(0, plays);
                FlashOverlay.FinishBurst(newBurst);
                Assert.False(FlashOverlay.IsBusy);
            }
            else if (phase == 1)
            {
                Assert.Equal(2, await FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation));
                CoreEngine.ApplyLive("flash", false);
                Assert.Equal(1, stops);
            }
            else
            {
                CoreAudio.PlayStoppableProvider = (_, _, _, start, _) =>
                {
                    CoreEngine.ApplyLive("flash", false);
                    start!(TimeSpan.FromSeconds(2));
                    return () => stops++;
                };
                Assert.Null(await FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation));
                Assert.Equal(1, stops);
            }
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            running.SetValue(null, oldRunning);
            (CoreFlash.StopProvider, CoreDispatch.PostProvider, FlashOverlay.VoiceLead, FlashOverlay.VoiceDuration, CoreAudio.PlayStoppableProvider) =
                (oldStop, oldPost, oldLead, oldDuration, oldPlay);
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Zero_reported_length_uses_the_probe_and_late_start_is_not_cancelled()
    {
        var file = Path.GetTempFileName();
        var (oldDuration, oldPlay, oldAfter, oldEngine, oldDuck, oldClock) = (FlashOverlay.VoiceDuration, CoreAudio.PlayStoppableProvider,
            FlashOverlay.AfterVoice, BarkHead.Engine, CoreAudio.DuckProvider, FlashOverlay.VoiceClock);
        var s = CoreSettings.Current;
        var (avatar, suppression) = (s.AvatarEnabled, s.BarkChatSuppressionMs);
        try
        {
            s.AvatarEnabled = true; s.BarkChatSuppressionMs = 0;
            Action<TimeSpan>? probeStart = null; Action? probeEnd = null;
            CoreAudio.PlayStoppableProvider = (_, gain, tag, start, end) =>
            { Assert.Equal(0, gain); Assert.Equal("flash-voice", tag); probeStart = start; probeEnd = end; return () => probeEnd!(); };
            var probe = oldDuration(file);
            probeStart!(TimeSpan.Zero); Assert.False(probe.IsCompleted);
            probeStart(TimeSpan.FromSeconds(6)); Assert.Equal(6, await probe);
            var rule = new BarkRule { Id = Guid.NewGuid().ToString(), Trigger = "AttentionCheckFail",
                VariantPool = new() { new BarkVariant("eyes") } };
            var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => { } };
            BarkHead.Seed(engine); BarkHead.Engine = engine; engine.Start();
            Action<TimeSpan>? start = null;
            int stops = 0, unduckMs = 0;
            FlashOverlay.VoiceDuration = _ => Task.FromResult<double?>(7.5);
            var clock = new VoiceClock();
            FlashOverlay.VoiceClock = clock;
            FlashOverlay.AfterVoice = (ms, _) => unduckMs = ms;
            CoreAudio.DuckProvider = _ => { };
            CoreAudio.PlayStoppableProvider = (_, _, _, callback, _) => { start = callback; return () => stops++; };
            var settings = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = true };
            var play = FlashOverlay.PlayFlashVoice(settings, file, FlashOverlay.Generation);
            Assert.False(engine.Raise("AttentionCheckFail")); // held while startup has no duration
            await Task.Delay(1200);
            Assert.False(play.IsCompleted); Assert.Equal(0, stops);
            start!(TimeSpan.Zero);
            Assert.Equal(7.5, await play);
            Assert.Equal(9000, unduckMs);
            Assert.False(engine.Raise("AttentionCheckFail")); // held after the bubble could have gone
            FlashOverlay.CloseAll(false);
            FlashOverlay.VoiceDuration = _ => Task.FromResult<double?>(null);
            play = FlashOverlay.PlayFlashVoice(settings, file, FlashOverlay.Generation);
            start!(TimeSpan.Zero);
            Assert.False(play.IsCompleted);
            clock.Ticks += TimeSpan.FromSeconds(2).Ticks;
            start!(TimeSpan.FromSeconds(6)); // a later LengthChanged supplies the duration
            Assert.Equal(4, await play); // the first two seconds already played
            Assert.Equal(5500, unduckMs);
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            (FlashOverlay.VoiceDuration, CoreAudio.PlayStoppableProvider, FlashOverlay.AfterVoice, BarkHead.Engine, CoreAudio.DuckProvider, FlashOverlay.VoiceClock) =
                (oldDuration, oldPlay, oldAfter, oldEngine, oldDuck, oldClock);
            (s.AvatarEnabled, s.BarkChatSuppressionMs) = (avatar, suppression);
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Zero_gain_flash_reaches_the_real_backend_and_retains_its_duration()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-silent-").FullName;
        var path = Path.Combine(dir, "voice.wav");
        var output = Path.Combine(dir, "audible.wav");
        LibVlcAudio audio;
        try { audio = new LibVlcAudio("--aout=file", "--audiofile-file=" + output, "--audiofile-wav"); }
        catch { Directory.Delete(dir, true); throw; }
        var oldProvider = CoreAudio.PlayStoppableProvider;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            const int bytes = 48000;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(bytes);
            for (var i = 0; i < bytes / 2; i++) w.Write((short)12000);
        }
        var started = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CoreAudio.PlayStoppableProvider = audio.PlayOneShot;
            var parsed = await FlashOverlay.VoiceDuration(path).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.InRange(parsed!.Value, 2.9, 3.1);
            var stop = audio.PlayOneShot(path, 0f, "flash-voice", t => { if (t > TimeSpan.Zero) started.TrySetResult(t); }, () => ended.TrySetResult());
            try
            {
                Assert.InRange((await started.Task.WaitAsync(TimeSpan.FromSeconds(8))).TotalSeconds, 2.9, 3.1);
                await Task.Delay(300); // allow a real output to open before checking the route
                Assert.False(File.Exists(output)); // this player uses dummy, not the configured audible output
            }
            finally { stop(); await ended.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        }
        finally { CoreAudio.PlayStoppableProvider = oldProvider; Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Cancelled_burst_stops_a_late_start_without_ducking()
    {
        var file = Path.GetTempFileName();
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldDuck = CoreAudio.DuckProvider;
        var oldDuration = FlashOverlay.VoiceDuration;
        FlashOverlay.VoiceDuration = _ => Task.FromResult<double?>(null);
        try
        {
            Action<TimeSpan>? start = null;
            int stops = 0, ducks = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, onStarted, _) => { start = onStarted; return () => stops++; };
            CoreAudio.DuckProvider = _ => ducks++;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = true };
            var play = FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation);
            FlashOverlay.CloseAll(false);
            start!(TimeSpan.FromSeconds(2));
            Assert.Null(await play);
            Assert.Equal(1, stops);
            Assert.Equal(0, ducks);
        }
        finally { CoreAudio.PlayStoppableProvider = oldPlay; CoreAudio.DuckProvider = oldDuck; FlashOverlay.VoiceDuration = oldDuration; File.Delete(file); }
    }
}
