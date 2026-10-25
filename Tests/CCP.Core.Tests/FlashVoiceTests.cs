using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class FlashVoiceTests
{
    [Fact]
    public void Pool_merges_library_and_recursive_extras_by_filename_and_honours_phrase_toggles()
    {
        var s = new AppSettings();
        s.DisabledPhraseIds.Add(CompanionPhraseIds.VoiceLineId("off.mp3"));
        s.RemovedPhraseIds.Add(CompanionPhraseIds.VoiceLineId("gone.wav"));
        Assert.Equal(new[] { "install/one.mp3", "mod/category/two.ogg" }, FlashVoice.Pool(s,
            new[] { "install/one.mp3", "install/off.mp3" },
            new[] { "pack/ONE.mp3", "mod/category/two.ogg", "mod/gone.wav" }));
    }

    [Fact]
    public void Mute_gates_leave_images_available_and_voice_level_zero_keeps_audio_timing()
    {
        var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100, CompanionVoiceLevel = 80 };
        Assert.True(FlashVoice.Enabled(s));
        Assert.Equal(0.85f, FlashVoice.Volume(s), 4);
        s.MasterVolume = 1;
        Assert.Equal(0.05f * 0.85f, FlashVoice.Volume(s), 4);
        s.MasterVolume = 0;
        Assert.False(FlashVoice.Enabled(s));
        Assert.Equal(0, FlashVoice.Volume(s));
        s.MasterVolume = 100;
        s.AvatarMuted = true;
        Assert.False(FlashVoice.Enabled(s));
        s.AvatarMuted = false;
        s.CompanionVoiceLinesMuted = true;
        Assert.False(FlashVoice.Enabled(s));
        s.CompanionVoiceLinesMuted = false;
        s.AvatarEnabled = false;
        s.CompanionVoiceLevel = 0;
        Assert.True(FlashVoice.Enabled(s));
        Assert.Equal(0, FlashVoice.Volume(s));
        s.FlashAudioEnabled = false;
        Assert.False(FlashVoice.Enabled(s));
    }

    [Fact]
    public void Audio_length_overrides_authored_duration_but_not_ambient_stay_safety()
    {
        var s = new AppSettings { FlashDuration = 5, FlashClickable = true, FlashStayUntilPopped = true };
        Assert.Equal(TimeSpan.FromSeconds(13), FlashVoice.Lifetime(s, false, 2000, TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromMinutes(10), FlashVoice.Lifetime(s, true, null, TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromSeconds(3), FlashVoice.Lifetime(s, false, 2000, null));
        s.FlashClickable = false;
        Assert.Equal(TimeSpan.FromSeconds(6), FlashVoice.Lifetime(s, true, null, null));
    }

    [Fact]
    public async Task Playback_uses_one_clip_duration_and_duck_lease_and_stop_releases_only_once()
    {
        var old = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider);
        var file = Path.GetTempFileName();
        var voice = new FlashVoice();
        int plays = 0, stops = 0, ducks = 0, restores = 0;
        try
        {
            CoreAudio.PlayStoppableProvider = (path, gain, tag, started, finished) =>
            {
                Assert.Equal(file, path);
                Assert.Equal("flash-voice", tag);
                plays++;
                started!(TimeSpan.FromSeconds(9));
                return () => stops++;
            };
            CoreAudio.DuckProvider = strength => { Assert.Equal(65, strength); ducks++; };
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = generation => { Assert.Equal(42, generation); restores++; };
            var s = new AppSettings { FlashAudioEnabled = true, AudioDuckingEnabled = true, DuckingLevel = 65, MasterVolume = 100 };
            Assert.Equal(TimeSpan.FromSeconds(9), await voice.PlayAsync(file, s));
            Assert.Equal((1, 0, 1, 0), (plays, stops, ducks, restores));
            voice.Stop();
            voice.Stop();
            Assert.Equal((1, 1, 1, 1), (plays, stops, ducks, restores));
        }
        finally
        {
            voice.Stop();
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider) = old;
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Duck_restores_after_clip_plus_tail_without_a_stop()
    {
        var old = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider);
        var file = Path.GetTempFileName();
        var voice = new FlashVoice();
        var restored = new TaskCompletionSource();
        try
        {
            CoreAudio.PlayStoppableProvider = (_, _, _, started, _) =>
            {
                started!(TimeSpan.FromMilliseconds(1));
                return () => { };
            };
            CoreAudio.DuckProvider = _ => { };
            CoreAudio.UnduckProvider = _ => restored.TrySetResult();
            await voice.PlayAsync(file, new AppSettings { FlashAudioEnabled = true, AudioDuckingEnabled = true, MasterVolume = 100 });
            Assert.False(restored.Task.IsCompleted);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            voice.Stop();
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider) = old;
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Failed_or_retired_playback_does_not_duck_or_change_lifetime()
    {
        var old = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider);
        var file = Path.GetTempFileName();
        var voice = new FlashVoice();
        Action<TimeSpan>? start = null;
        Action? finish = null;
        try
        {
            CoreAudio.DuckProvider = _ => Assert.Fail("Failed playback must not duck");
            var s = new AppSettings { FlashAudioEnabled = true, AudioDuckingEnabled = true, MasterVolume = 100 };
            CoreAudio.PlayStoppableProvider = (_, _, _, _, ended) => { ended!(); return () => { }; };
            Assert.Null(await voice.PlayAsync(file, s));
            CoreAudio.PlayStoppableProvider = (_, _, _, started, ended) => { start = started; finish = ended; return () => { }; };
            var pending = voice.PlayAsync(file, s);
            voice.Stop();
            start!(TimeSpan.FromSeconds(9));
            finish!();
            Assert.Null(await pending);
        }
        finally
        {
            voice.Stop();
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider) = old;
            File.Delete(file);
        }
    }
}
