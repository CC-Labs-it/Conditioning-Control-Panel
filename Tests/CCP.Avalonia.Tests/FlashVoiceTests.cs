using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
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
        }
        finally
        {
            FlashOverlay.AfterVoice = oldAfter;
            CoreAudio.PlayStoppableProvider = oldPlay;
            CoreAudio.DuckProvider = oldDuck;
            CoreAudio.UnduckProvider = oldUnduck;
            CoreAudio.DuckGenerationProvider = oldGeneration;
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Cancelled_burst_stops_a_late_start_without_ducking()
    {
        var file = Path.GetTempFileName();
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldDuck = CoreAudio.DuckProvider;
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
        finally { CoreAudio.PlayStoppableProvider = oldPlay; CoreAudio.DuckProvider = oldDuck; File.Delete(file); }
    }
}
