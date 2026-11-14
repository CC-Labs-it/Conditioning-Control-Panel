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
    [Theory]
    [InlineData(0, true, 0, 1, 4)]
    [InlineData(1, true, 0, 1, 8)]
    [InlineData(1, false, 1, 10, 60)]
    [InlineData(3, true, 0, 1, 4)]
    [InlineData(2, true, 0, 1, 8)]
    [InlineData(2, false, 1, 10, 60)]
    [InlineData(2, false, 2, 10, 10)]
    [InlineData(2, true, 2, 10, 80)]
    public async Task Flash_xp_uses_attempted_voice_and_keeps_multipliers(int playback, bool linked, int generation, int lucky, int expected)
    {
        var file = Path.GetTempFileName();
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldXp = CoreProgression.AddXPProvider;
        try {
            double awarded = 0;
            CoreProgression.AddXPProvider = (xp, _) => awarded += xp;
            CoreAudio.PlayStoppableProvider = (_, _, _, start, finish) => {
                if (playback == 2) start!(TimeSpan.Zero); else finish!();
                return () => { };
            };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AvatarMuted = playback == 0,
                HydraLinkedTiming = linked, SuppressPerkNotifications = true };
            var burst = FlashOverlay.BeginBurst();
            await FlashOverlay.PlayFlashVoice(s, playback == 3 ? file + ".missing" : file, burst);
            FlashOverlay.Paid(file, s, generation, lucky);
            Assert.Equal(expected, awarded);
            FlashOverlay.FinishBurst(burst);
            FlashOverlay.CloseAll(false);   // WPF FlashService.cs:5118 clears the voiced flag on close
            awarded = 0;
            FlashOverlay.Paid(file, s, 0, 1);
            Assert.Equal(4, awarded);
        } finally {
            FlashOverlay.CloseAll(false); CoreAudio.PlayStoppableProvider = oldPlay;
            CoreProgression.AddXPProvider = oldXp; File.Delete(file);
        }
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
        var muted = s.AvatarMuted; var enabled = s.AvatarEnabled;
        s.AvatarMuted = false; s.AvatarEnabled = true; // the trunk bubble needs a visible avatar (WPF Speech.cs:481)
        var tube = new AvatarTubeWindow(null);
        try {
            // The trunk types preset bubbles out (WPF StartTypewriter), so read the line the bubble holds.
            var length = typeof(AvatarTubeWindow).GetField("_currentSpeechLength", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var bubble = tube.FindControl<Border>("SpeechBubble")!;
            tube.Show(); tube.ShowFlashVoice("first clip"); Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible); Assert.Equal("first clip".Length, (int)length.GetValue(tube)!);
            tube.ShowFlashVoice("second clip"); Dispatcher.UIThread.RunJobs(); Assert.Equal("first clip".Length, (int)length.GetValue(tube)!);
        } finally { tube.Close(); s.AvatarMuted = muted; s.AvatarEnabled = enabled; }
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
        try {
            foreach (var file in new[] { "one.mp3", "off.wav", "removed.ogg", "nested/one.mp3", "nested/two.wav" })
                File.WriteAllText(Path.Combine(folder, file), "test");
            CoreMods.ActiveModIdProvider = () => "custom"; CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest(), root, false);
            var s = new AppSettings(); s.DisabledPhraseIds.Add("VoiceLine:off"); s.RemovedPhraseIds.Add("VoiceLine:removed"); var pool = FlashOverlay.VoicePool(s);
            Assert.Equal(2, pool.Count); Assert.Contains(Path.Combine(folder, "one.mp3"), pool); Assert.Contains(Path.Combine(folder, "nested", "two.wav"), pool);
        }
        finally {
            CoreMods.ActiveModPackageProvider = oldMod; CoreMods.ActiveModIdProvider = oldId; Directory.Delete(root, true);
        }
    }
    [Fact]
    public async Task Voice_obeys_mutes_and_ducks_for_clip_length_with_guarded_release()
    {
        var file = Path.GetTempFileName();
        var oldAfter = FlashOverlay.AfterVoice;
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldDuck = CoreAudio.DuckProvider;
        var oldUnduck = CoreAudio.UnduckProvider;
        var oldGeneration = CoreAudio.DuckGenerationProvider;
        try {
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, CompanionVoiceLevel = 80,
                AudioDuckingEnabled = true, DuckingLevel = 72, AvatarMuted = false, CompanionVoiceLinesMuted = false };
            int plays = 0, strength = 0, delay = 0, stops = 0; long released = -1; float gain = 0; Action? release = null;
            CoreAudio.PlayStoppableProvider = (path, vol, tag, start, finish) =>
            {
                plays++; gain = vol; Assert.Equal(file, path); Assert.Equal("flash-voice", tag); start!(TimeSpan.FromSeconds(3));
                return () => stops++;
            }; CoreAudio.DuckProvider = n => strength = n; CoreAudio.DuckGenerationProvider = () => 42; CoreAudio.UnduckProvider = n => released = n;
            FlashOverlay.AfterVoice = (ms, action) => { delay = ms; release = action; };

            Assert.Equal(3, await FlashOverlay.PlayFlashVoice(s, file)); Assert.Equal(1, plays); Assert.Equal((float)Math.Pow(0.5, 1.5) * 0.85f, gain, 5);
            Assert.Equal(72, strength); Assert.Equal(4500, delay); release!(); Assert.Equal(42, released); FlashOverlay.CloseAll(false); Assert.Equal(1, stops);
            s.FlashAudioEnabled = false; Assert.Null(await FlashOverlay.PlayFlashVoice(s, file)); s.FlashAudioEnabled = true; s.AvatarMuted = true;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file)); s.AvatarMuted = false; s.CompanionVoiceLinesMuted = true;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file)); s.CompanionVoiceLinesMuted = false; s.MasterVolume = 0;
            Assert.Null(await FlashOverlay.PlayFlashVoice(s, file)); Assert.Equal(1, plays); s.MasterVolume = 50; s.CompanionVoiceLevel = 0;
            Assert.Equal(3, await FlashOverlay.PlayFlashVoice(s, file)); Assert.Equal(0, gain); Assert.Equal(2, plays); Assert.Equal(4500, delay);
            FlashOverlay.CloseAll(false);
        }
        finally {
            FlashOverlay.AfterVoice = oldAfter; CoreAudio.PlayStoppableProvider = oldPlay; CoreAudio.DuckProvider = oldDuck; CoreAudio.UnduckProvider = oldUnduck;
            CoreAudio.DuckGenerationProvider = oldGeneration; File.Delete(file);
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    // On the dispatcher thread, as the feature card toggles it: off that thread CoreFlash.StopProvider posts a
    // second CloseAll, and the test dispatcher thread could run it between BeginBurst and the busy check
    // (the wave 4 Windows flake), retiring the new burst the test just began.
    public Task Live_flash_off_retires_a_pending_burst_and_stops_a_playing_voice(int phase) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var file = Path.GetTempFileName();
        var running = typeof(CoreEngine).GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldRunning = running.GetValue(null);
        var (oldStop, oldPost, oldLead, oldPlay) = (CoreFlash.StopProvider, CoreDispatch.PostProvider,
            FlashOverlay.VoiceLead, CoreAudio.PlayStoppableProvider);
        var lead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            int plays = 0, stops = 0; running.SetValue(null, true); CoreDispatch.PostProvider = action => action(); FlashOverlay.SeedCore(() => null, () => { });
            FlashOverlay.VoiceLead = () => lead.Task;
            CoreAudio.PlayStoppableProvider = (_, _, _, start, _) => { plays++; start!(TimeSpan.FromSeconds(2)); return () => stops++; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 };
            if (phase == 0)
            {
                var oldBurst = FlashOverlay.BeginBurst(); var pending = FlashOverlay.VoiceAfterLead(s, oldBurst, file); CoreEngine.ApplyLive("flash", false);
                Assert.False(FlashOverlay.IsBusy); var newBurst = FlashOverlay.BeginBurst(); FlashOverlay.FinishBurst(oldBurst); Assert.True(FlashOverlay.IsBusy);
                lead.SetResult(); Assert.Null(await pending); Assert.Equal(0, plays); FlashOverlay.FinishBurst(newBurst); Assert.False(FlashOverlay.IsBusy);
            }
            else if (phase == 1)
            {
                Assert.Equal(2, await FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation)); CoreEngine.ApplyLive("flash", false); Assert.Equal(1, stops);
            }
            else {
                CoreAudio.PlayStoppableProvider = (_, _, _, start, _) =>
                {
                    CoreEngine.ApplyLive("flash", false); start!(TimeSpan.FromSeconds(2));
                    return () => stops++;
                }; Assert.Null(await FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation)); Assert.Equal(1, stops);
            }
        }
        finally {
            FlashOverlay.CloseAll(false); running.SetValue(null, oldRunning);
            (CoreFlash.StopProvider, CoreDispatch.PostProvider, FlashOverlay.VoiceLead, CoreAudio.PlayStoppableProvider) =
                (oldStop, oldPost, oldLead, oldPlay);
            File.Delete(file);
        }
    });
    [Fact]
    public async Task Playback_starts_without_probe_and_zero_length_does_not_block_presentation_or_stop()
    {
        var file = Path.GetTempFileName();
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldAfter = FlashOverlay.AfterVoice;
        var oldDuck = CoreAudio.DuckProvider;
        var oldClock = FlashOverlay.VoiceClock; var oldEngine = BarkHead.Engine;
        var settings = CoreSettings.Current; var oldSettings = (settings.AvatarEnabled, settings.BarkChatSuppressionMs);
        try {
            settings.AvatarEnabled = true; settings.BarkChatSuppressionMs = 0;
            var rule = new BarkRule { Id = Guid.NewGuid().ToString(), Trigger = "AttentionCheckFail", VariantPool = new() { new BarkVariant("eyes") } };
            var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => { } }; BarkHead.Seed(engine); BarkHead.Engine = engine; engine.Start();
            var clock = new VoiceClock(); FlashOverlay.VoiceClock = clock; Action<TimeSpan>? report = null; int plays = 0, stops = 0, ducks = 0, delay = 0;
            CoreAudio.PlayStoppableProvider = (_, gain, _, start, _) => { Assert.True(gain > 0); plays++; report = start; return () => stops++; };
            CoreAudio.DuckProvider = _ => ducks++; FlashOverlay.AfterVoice = (ms, _) => delay = ms;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = true };
            var play = FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation); Assert.Equal(1, plays); Assert.False(engine.Raise("AttentionCheckFail"));
            report!(TimeSpan.Zero); Assert.Null(await play.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, ducks); Assert.True(FlashOverlay.VoiceStarting); Assert.False(engine.Raise("AttentionCheckFail"));
            clock.Ticks += TimeSpan.FromSeconds(2).Ticks; report(TimeSpan.FromSeconds(6));
            Assert.Equal(5500, delay); Assert.False(FlashOverlay.VoiceStarting); Assert.False(engine.Raise("AttentionCheckFail")); FlashOverlay.CloseAll(false);
            Assert.Equal(1, stops);
        }
        finally {
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, FlashOverlay.AfterVoice, CoreAudio.DuckProvider, FlashOverlay.VoiceClock, BarkHead.Engine) = (oldPlay, oldAfter, oldDuck, oldClock, oldEngine);
            (settings.AvatarEnabled, settings.BarkChatSuppressionMs) = oldSettings; File.Delete(file);
        }
    }
    [Fact]
    public Task Late_duration_resets_a_window_deadline_from_its_spawn() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = FlashOverlay.Clock; var clock = new VoiceClock { Ticks = TimeSpan.FromSeconds(5).Ticks }; FlashOverlay.Clock = clock;
        var window = new FlashOverlayWindow { SpawnedAt = clock.Ticks }; window.Run(0, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        try { window.BoostLifetime(2500); window.SetVoiceLifetime(TimeSpan.FromSeconds(1)); Assert.Equal(TimeSpan.FromSeconds(7.5).Ticks, window.ExpiresAt); clock.Ticks += TimeSpan.FromSeconds(2).Ticks; window.SetVoiceLifetime(TimeSpan.FromSeconds(7)); Assert.Equal(TimeSpan.FromSeconds(12).Ticks, window.ExpiresAt); window.BoostLifetime(9000); window.SetVoiceLifetime(TimeSpan.FromSeconds(7)); Assert.Equal(TimeSpan.FromSeconds(16).Ticks, window.ExpiresAt); Assert.Equal(TimeSpan.FromSeconds(7), window.Lifetime); }
        finally { window.Close(); FlashOverlay.Clock = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public async Task Late_voice_duration_updates_active_and_queued_picture_lifetimes()
    {
        var file = Path.GetTempFileName(); var old = CoreAudio.PlayStoppableProvider;
        try {
            Action<TimeSpan>? report = null; CoreAudio.PlayStoppableProvider = (_, _, _, start, _) => { report = start; return () => { }; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, FlashDuration = 2 }; var timing = new FlashOverlay.BurstLifetime(s, false, null);
            var active = timing.Current; timing.Track(lifetime => active = lifetime);
            var play = FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation, timing.Report); report!(TimeSpan.Zero); await play;
            Assert.Equal(TimeSpan.FromSeconds(3), active); report(TimeSpan.FromSeconds(6)); Assert.Equal(TimeSpan.FromSeconds(7), active);
            Assert.Equal(TimeSpan.FromSeconds(7), timing.Current);
            report(TimeSpan.FromSeconds(1));
            Assert.Equal(TimeSpan.FromSeconds(2), active); Assert.Equal(active, timing.Current);
            var stays = new FlashOverlay.BurstLifetime(new AppSettings { FlashClickable = true, FlashStayUntilPopped = true }, true, null);
            stays.Report(6); Assert.Equal(TimeSpan.FromMinutes(10), stays.Current);
        } finally { FlashOverlay.CloseAll(false); CoreAudio.PlayStoppableProvider = old; File.Delete(file); }
    }
    [Fact]
    public async Task Late_completion_releases_only_the_flash_duck()
    {
        var file = Path.GetTempFileName();
        var (playWas, duckWas, unduckWas, afterWas) = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, FlashOverlay.AfterVoice);
        var timers = new System.Collections.Generic.List<Action>();
        Action? finish = null; int ducks = 0;
        try {
            CoreAudio.PlayStoppableProvider = (_, _, _, start, end) => { finish = end; start!(TimeSpan.FromSeconds(1)); return () => { }; };
            CoreAudio.DuckProvider = _ => ducks++; CoreAudio.UnduckProvider = _ => ducks--; FlashOverlay.AfterVoice = (_, action) => timers.Add(action);
            await FlashOverlay.PlayFlashVoice(new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = true }, file); Assert.Equal(1, ducks);
            CoreAudio.Duck(50); // a second voice owns its own duck
            timers[0](); Assert.Equal(1, ducks); finish!();
            foreach (var timer in timers.ToArray()) timer();
            Assert.Equal(1, ducks);
        }
        finally {
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, FlashOverlay.AfterVoice) = (playWas, duckWas, unduckWas, afterWas);
            File.Delete(file);
        }
    }
    [Fact]
    public async Task Unknown_playback_length_does_not_delay_start()
    {
        var file = Path.GetTempFileName(); var old = CoreAudio.PlayStoppableProvider;
        try {
            CoreAudio.PlayStoppableProvider = (_, gain, _, start, finish) => { if (gain == 0) finish!(); else start!(TimeSpan.Zero); return () => finish!(); };
            var play = FlashOverlay.PlayFlashVoice(new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 }, file, FlashOverlay.Generation);
            Assert.Same(play, await Task.WhenAny(play, Task.Delay(2000)));
        } finally { FlashOverlay.CloseAll(false); CoreAudio.PlayStoppableProvider = old; File.Delete(file); }
    }
    [Fact]
    public async Task Stop_cancels_voice_even_before_a_start_callback()
    {
        var file = Path.GetTempFileName(); var old = CoreAudio.PlayStoppableProvider; int stops = 0;
        try {
            CoreAudio.PlayStoppableProvider = (_, _, _, _, finish) => () => { stops++; finish!(); };
            var play = FlashOverlay.PlayFlashVoice(new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 }, file, FlashOverlay.Generation);
            FlashOverlay.CloseAll(false); Assert.Equal(1, stops); Assert.Same(play, await Task.WhenAny(play, Task.Delay(2000)));
        } finally { FlashOverlay.CloseAll(false); CoreAudio.PlayStoppableProvider = old; File.Delete(file); }
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
            const int bytes = 48000; w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
            for (var i = 0; i < bytes / 2; i++) w.Write((short)12000);
        }
        var started = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try {
            CoreAudio.PlayStoppableProvider = audio.PlayOneShot;
            var stop = audio.PlayOneShot(path, 0f, "flash-voice", t => { if (t > TimeSpan.Zero) started.TrySetResult(t); }, () => ended.TrySetResult());
            try {
                Assert.InRange((await started.Task.WaitAsync(TimeSpan.FromSeconds(8))).TotalSeconds, 2.9, 3.1);
                await Task.Delay(300); // allow a real output to open before checking the route
                Assert.False(File.Exists(output)); // this player uses dummy, not the configured audible output
            } finally { stop(); await ended.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        } finally { CoreAudio.PlayStoppableProvider = oldProvider; Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task Cancelled_burst_stops_a_late_start_without_ducking()
    {
        var file = Path.GetTempFileName();
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldDuck = CoreAudio.DuckProvider;
        try {
            Action<TimeSpan>? start = null; int stops = 0, ducks = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, onStarted, _) => { start = onStarted; return () => stops++; }; CoreAudio.DuckProvider = _ => ducks++;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, AudioDuckingEnabled = true };
            var play = FlashOverlay.PlayFlashVoice(s, file, FlashOverlay.Generation); FlashOverlay.CloseAll(false); start!(TimeSpan.FromSeconds(2));
            Assert.Null(await play); Assert.Equal(1, stops); Assert.Equal(0, ducks);
        } finally { CoreAudio.PlayStoppableProvider = oldPlay; CoreAudio.DuckProvider = oldDuck; File.Delete(file); }
    }
}
