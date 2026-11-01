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
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

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
        var (was, clock, busy) = (CoreAudio.PlayStoppableProvider, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil);
        FlashOverlay.VoiceClock = new Clock();
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
        finally
        {
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil) = (was, clock, busy);
            File.Delete(path);
        }
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
        var (clock, busy) = (FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil);
        FlashOverlay.VoiceClock = new Clock();
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
            (FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil) = (clock, busy);
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
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => { };
            typeof(FlashOverlay).GetField("_busy", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, true);
            var silent = FlashOverlay.StartVoice(new AppSettings { FlashAudioEnabled = true, MasterVolume = 50 }, path, FlashOverlay.Generation);
            FlashOverlay.CloseAll(false); Assert.False(FlashOverlay.IsBusy);
            Assert.Null(await silent.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task Delayed_length_keeps_whisper_busy_and_sets_remaining_unduck_time()
    {
        var path = Path.GetTempFileName();
        var (play, duck, unduck, gen, delay, clock, busy) = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider,
            CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, FlashOverlay.UnduckDelay, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil);
        var fakeClock = new Clock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<TimeSpan>? report = null;
        TimeSpan wait = default;
        try
        {
            FlashOverlay.VoiceClock = fakeClock;
            FlashOverlay.WhisperBusyUntil = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, started, _) => { report = started; started!(TimeSpan.Zero); return () => { }; };
            CoreAudio.DuckProvider = _ => { };
            CoreAudio.DuckGenerationProvider = () => 7;
            CoreAudio.UnduckProvider = _ => { };
            FlashOverlay.UnduckDelay = t => { wait = t; return release.Task; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = true };
            var pending = FlashOverlay.StartVoice(s, path, FlashOverlay.Generation);
            Assert.False(pending.IsCompleted);
            Assert.True(FlashOverlay.IsWhisperPlaying);
            fakeClock.Now += TimeSpan.FromSeconds(2);
            report!(TimeSpan.FromSeconds(8));
            var remaining = await pending;
            Assert.Equal(TimeSpan.FromSeconds(6), remaining);
            Assert.Equal(TimeSpan.FromSeconds(7), FlashOverlay.Lifetime(s, false, (int)remaining!.Value.TotalMilliseconds));
            Assert.Equal(TimeSpan.FromSeconds(7.5), wait);
            fakeClock.Now += TimeSpan.FromSeconds(6.2);
            Assert.True(FlashOverlay.IsWhisperPlaying);
            fakeClock.Now += TimeSpan.FromMilliseconds(51);
            Assert.False(FlashOverlay.IsWhisperPlaying);
        }
        finally
        {
            release.TrySetResult(); FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                FlashOverlay.UnduckDelay, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil) = (play, duck, unduck, gen, delay, clock, busy);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Playback_failure_uses_five_seconds_and_still_marks_the_whisper()
    {
        var path = Path.GetTempFileName();
        var (play, clock, busy, show) = (CoreAudio.PlayStoppableProvider, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil, FlashOverlay.ShowVoice);
        string? shown = null;
        try
        {
            FlashOverlay.ShowVoice = p => shown = p;
            FlashOverlay.VoiceClock = new Clock(); FlashOverlay.WhisperBusyUntil = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, finished) => { finished!(); return () => { }; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = false };
            var duration = await FlashOverlay.StartVoice(s, path, FlashOverlay.Generation);
            Assert.Equal(TimeSpan.FromSeconds(5), duration);
            Assert.True(FlashOverlay.IsWhisperPlaying);
            Assert.Equal(path, shown);
            Assert.Equal(TimeSpan.FromSeconds(6), FlashOverlay.Lifetime(s, false, (int)duration!.Value.TotalMilliseconds));
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil, FlashOverlay.ShowVoice) = (play, clock, busy, show);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StopWhileLengthIsUnknownReleasesTheDuckEvenIfBackendNeverFinishes()
    {
        var path = Path.GetTempFileName();
        var (play, duck, unduck, delay, clock, busy) = (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider,
            CoreAudio.UnduckProvider, FlashOverlay.UnduckDelay, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            FlashOverlay.VoiceClock = new Clock(); FlashOverlay.WhisperBusyUntil = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, started, _) => { started!(TimeSpan.Zero); return () => { }; };
            CoreAudio.DuckProvider = _ => { };
            CoreAudio.UnduckProvider = _ => released.TrySetResult();
            FlashOverlay.UnduckDelay = _ => Task.CompletedTask;
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = true };
            var pending = FlashOverlay.StartVoice(s, path, FlashOverlay.Generation);
            Assert.False(pending.IsCompleted);
            FlashOverlay.CloseAll(false);
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            await released.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            (CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider,
                FlashOverlay.UnduckDelay, FlashOverlay.VoiceClock, FlashOverlay.WhisperBusyUntil) = (play, duck, unduck, delay, clock, busy);
            File.Delete(path);
        }
    }


}
