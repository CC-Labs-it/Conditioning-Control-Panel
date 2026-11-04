using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
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

    [Fact]
    public void BurstStartsOneLineAndStopReleasesItOnce()
    {
        var old = CoreAudio.PlayStoppableProvider;
        var played = new List<string>();
        var stops = 0;
        try
        {
            CoreAudio.PlayStoppableProvider = (path, _, _, _, _) => { played.Add(path); return () => stops++; };
            var s = new AppSettings { FlashAudioEnabled = true, MasterVolume = 100 };
            FlashVoice.Play(s, new[] { "one.mp3", "two.mp3" });
            Assert.Single(played);
            FlashVoice.Stop();
            FlashVoice.Stop();
            Assert.Equal(1, stops);
        }
        finally { FlashVoice.Stop(); CoreAudio.PlayStoppableProvider = old; }
    }
}
