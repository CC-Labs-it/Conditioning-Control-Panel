using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashVoiceTests
{
    [Fact]
    public void Pool_keeps_library_and_enabled_recursive_clips_without_duplicate_names()
    {
        var s = new AppSettings();
        s.DisabledPhraseIds.Add("VoiceLine:off");
        s.RemovedPhraseIds.Add("VoiceLine:gone");
        var library = new[] { "/library/one.mp3" };
        var extra = new[] { "/folder/ONE.mp3", "/folder/off.wav", "/folder/gone.ogg", "/folder/nested/two.wav", "/folder/no.txt" };
        Assert.Equal(new[] { "/library/one.mp3", "/folder/nested/two.wav" }, FlashOverlay.VoicePool(s, library, extra));
    }

    [Fact]
    public async Task Voice_honours_mutes_and_the_companion_volume_curve()
    {
        var path = Path.GetTempFileName();
        var was = CoreAudio.PlayStoppableProvider;
        var calls = 0;
        float volume = -1;
        try
        {
            CoreAudio.PlayStoppableProvider = (_, v, _, started, _) => { calls++; volume = v; started!(TimeSpan.FromSeconds(2)); return () => { }; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 25, CompanionVoiceLevel = 80, AudioDuckingEnabled = false };
            foreach (var mute in new Action[] { () => s.FlashAudioEnabled = false, () => s.MasterVolume = 0,
                         () => s.AvatarMuted = true, () => s.CompanionVoiceLinesMuted = true })
            {
                s.FlashAudioEnabled = true; s.MasterVolume = 25; s.AvatarMuted = false; s.CompanionVoiceLinesMuted = false;
                mute();
                Assert.Null(await FlashOverlay.StartVoice(s, path, FlashOverlay.Generation));
            }
            Assert.Equal(0, calls);
            s.FlashAudioEnabled = true; s.MasterVolume = 25; s.AvatarMuted = false; s.CompanionVoiceLinesMuted = false;
            Assert.Equal(TimeSpan.FromSeconds(2), await FlashOverlay.StartVoice(s, path, FlashOverlay.Generation));
            Assert.Equal(1, calls);
            Assert.Equal(0.10625f, volume, 5);
        }
        finally { FlashOverlay.CloseAll(false); CoreAudio.PlayStoppableProvider = was; File.Delete(path); }
    }

    [Fact]
    public async Task Voice_length_and_xp_follow_the_clip_and_stop_does_not_cancel_unduck()
    {
        var path = Path.GetTempFileName();
        var (play, duck, unduck, gen, delay, xpProvider) = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider,
            CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, FlashOverlay.UnduckDelay, CoreProgression.AddXPProvider);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0; var strength = 0; TimeSpan wait = default; double xp = 0;
        try
        {
            CoreAudio.PlayStoppableProvider = (_, _, _, started, _) => { started!(TimeSpan.FromSeconds(2)); return () => stops++; };
            CoreAudio.DuckProvider = n => strength = n;
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = n => released.TrySetResult(n);
            FlashOverlay.UnduckDelay = t => { wait = t; return release.Task; };
            CoreProgression.AddXPProvider = (n, _) => xp = n;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = true, DuckingLevel = 73, FlashDuration = 9 };
            var length = await FlashOverlay.StartVoice(s, path, FlashOverlay.Generation);
            Assert.Equal(TimeSpan.FromSeconds(2), length);
            Assert.Equal(TimeSpan.FromSeconds(3), FlashOverlay.Lifetime(s, false, (int)length!.Value.TotalMilliseconds));
            FlashOverlay.Paid("image.png", s, 0, 1);
            Assert.Equal(8, xp);
            Assert.Equal(73, strength);
            Assert.Equal(TimeSpan.FromMilliseconds(3500), wait);
            FlashOverlay.CloseAll(false);
            Assert.Equal(1, stops);
            Assert.False(released.Task.IsCompleted);
            release.SetResult();
            Assert.Equal(42, await released.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult();
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                FlashOverlay.UnduckDelay, CoreProgression.AddXPProvider) = (play, duck, unduck, gen, delay, xpProvider);
            File.Delete(path);
        }
    }
    [Fact]
    public async Task Stop_before_audio_starts_cannot_duck_or_revive_the_retired_burst()
    {
        var path = Path.GetTempFileName();
        var (play, duck) = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider);
        Action<TimeSpan>? started = null;
        var stops = 0; var ducks = 0;
        try
        {
            CoreAudio.PlayStoppableProvider = (_, _, _, onStarted, _) => { started = onStarted; return () => stops++; };
            CoreAudio.DuckProvider = _ => ducks++;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = true };
            var pending = FlashOverlay.StartVoice(s, path, FlashOverlay.Generation);
            FlashOverlay.CloseAll(false);
            started!(TimeSpan.FromSeconds(2));
            Assert.Null(await pending);
            Assert.Equal(1, stops);
            Assert.Equal(0, ducks);
        }
        finally { FlashOverlay.CloseAll(false); (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider) = (play, duck); File.Delete(path); }
    }

}
