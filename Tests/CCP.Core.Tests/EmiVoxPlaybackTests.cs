using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class EmiVoxPlaybackTests : IDisposable
{
    private readonly int _master = CoreSettings.Current.MasterVolume;
    private readonly Func<string, float, string, Action<TimeSpan>?, Action?, Action?>? _play = CoreAudio.PlayStoppableProvider;
    private readonly Action<string, float, string, Action<TimeSpan>?, Action?>? _oneShot = CoreAudio.PlayOneShotProvider;
    private readonly Func<bool>? _suppressed = CoreAudio.IsOutputSuppressedProvider;
    private readonly List<(string Path, float Volume, string Tag)> _clips = new();
    private int _stops;

    public EmiVoxPlaybackTests()
    {
        CoreSettings.Current.MasterVolume = 37;
        CoreAudio.IsOutputSuppressedProvider = () => false;
        CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
        {
            _clips.Add((path, volume, tag));
            return () => _stops++;
        };
        CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => _clips.Add((path, volume, tag));
        EmiLineEngine.Instance.ResetForTests();
    }

    [Fact]
    public void OneCachedWavPerLineAndMoodWithTicksOutsideTheLiveSlot()
    {
        using var voice = new EmiVox();
        Assert.True(voice.Speak("book margin?", "sad"));
        var first = Assert.Single(_clips);
        Assert.Equal((.37f, "emi-vox"), (first.Volume, first.Tag));
        var wav = File.ReadAllBytes(first.Path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.True(wav.Length > 44);
        Assert.True(voice.Tick());
        Assert.Equal("emi-vox-tick", _clips[1].Tag);
        Assert.Equal(0, _stops);
        Assert.True(voice.Speak("book margin?", "sad"));
        Assert.Equal(first.Path, _clips[2].Path);
        Assert.Equal(wav, File.ReadAllBytes(first.Path));
        Assert.Equal(1, _stops);
        Assert.True(voice.Speak("book margin?", "celebration"));
        Assert.NotEqual(first.Path, _clips[3].Path);
        voice.Stop();
        voice.Stop();
        Assert.Equal(3, _stops);
        voice.Dispose();
        Assert.False(voice.Speak("after close", "idle"));
        Assert.False(voice.Tick());
    }

    [Fact]
    public void MasterBreakerAndSafetyHoldsSilenceBothKindsOfSound()
    {
        using var voice = new EmiVox();
        CoreSettings.Current.MasterVolume = 0;
        Assert.False(voice.Tick());
        Assert.False(voice.Speak("muted", "idle"));
        CoreSettings.Current.MasterVolume = 37;
        CoreAudio.IsOutputSuppressedProvider = () => true;
        Assert.False(voice.Tick());
        Assert.False(voice.Speak("breaker", "idle"));
        CoreAudio.IsOutputSuppressedProvider = () => false;
        EmiLineEngine.Instance.Draw("lockdownCountdown");
        Assert.True(EmiLineEngine.Instance.HoldActive);
        Assert.False(voice.Tick());
        Assert.False(voice.Speak("held", "idle"));
        Assert.Empty(_clips);
    }

    [Theory]
    [InlineData("^_^", "celebration")]
    [InlineData(";_;", "sad")]
    [InlineData(">_<", "shock")]
    [InlineData("B)", "smug")]
    [InlineData("<3", "pet")]
    [InlineData("0_0", "idle")]
    [InlineData(null, "idle")]
    [InlineData("unknown", "idle")]
    public void ReactionFacesUseTheCanonVoiceMood(string? face, string mood) =>
        Assert.Equal(mood, EmiSpeech.FrameForFace(face));

    public void Dispose()
    {
        CoreSettings.Current.MasterVolume = _master;
        (CoreAudio.PlayStoppableProvider, CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider) =
            (_play, _oneShot, _suppressed);
        EmiLineEngine.Instance.ResetForTests();
    }
}
