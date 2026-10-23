using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>No UI or audio device: real cue resolution through the existing mod/audio seams.</summary>
public sealed class SpiralRoomSfxTests : IDisposable
{
    private readonly Func<string, string?>? _oldResolver = CoreModArt.AudioOverridePathProvider;
    private readonly Action<string, float, string, Action<TimeSpan>?, Action?>? _oldAudio = CoreAudio.PlayOneShotProvider;
    private readonly DescentCountdownService? _oldFuse = Head.DescentCountdown;
    private readonly int _oldVolume = CoreSettings.Current.MasterVolume;
    private readonly bool _oldEnabled = CoreSettings.Current.DescentCountdownAudio;
    private readonly string _asset = Path.Combine(TestUserDataProfile.Root, "spiral-cue.mp3");
    private readonly List<(string Path, float Volume, string Tag)> _played = new();

    public SpiralRoomSfxTests()
    {
        File.WriteAllBytes(_asset, Array.Empty<byte>());
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.DescentCountdownAudio = true;
        Head.DescentCountdown = new DescentCountdownService();
        CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => _played.Add((path, volume, tag));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void DedicatedCueWins_OtherwiseUsesTheShippedFallback(bool splash, bool dedicatedExists)
    {
        var dedicated = splash ? "chaos/descent_spiral_open.mp3" : "chaos/descent_fog_enter.mp3";
        var fallback = splash ? "chaos/reveal_chime.mp3" : "chaos/ui_deepen.mp3";
        var probes = new List<string>();
        CoreModArt.AudioOverridePathProvider = rel =>
        {
            probes.Add(rel);
            return rel == dedicated && !dedicatedExists ? _asset + ".missing" : _asset;
        };
        Play(splash);
        var played = Assert.Single(_played);
        Assert.Equal(_asset, played.Path);
        Assert.Equal(splash ? 0.17f : 0.21f, played.Volume, 5);
        Assert.Equal(splash ? "descent-spiral-open" : "descent-fog-enter", played.Tag);
        Assert.Equal(dedicatedExists ? new[] { dedicated } : new[] { dedicated, fallback }, probes);
    }

    [Theory]
    [InlineData(false, 50)]
    [InlineData(true, 0)]
    public void UserSilenceStopsBothCuesBeforeResolving(bool enabled, int volume)
    {
        CoreSettings.Current.DescentCountdownAudio = enabled;
        CoreSettings.Current.MasterVolume = volume;
        var probes = 0;
        CoreModArt.AudioOverridePathProvider = _ => { probes++; return _asset; };
        Play(false);
        Play(true);
        Assert.Empty(_played);
        Assert.Equal(0, probes);
    }

    [Fact]
    public void GatesAreReadAgainForTheNextCue_AndRespectTheMasterVolumeCap()
    {
        CoreModArt.AudioOverridePathProvider = _ => _asset;
        CoreSettings.Current.DescentCountdownAudio = false;
        Play(false);
        CoreSettings.Current.DescentCountdownAudio = true;
        CoreSettings.Current.MasterVolume = 1000;
        Play(true);
        Assert.Equal(100, CoreSettings.Current.MasterVolume);
        Assert.Equal(0.34f, Assert.Single(_played).Volume, 5);
        CoreSettings.Current.MasterVolume = 0;
        Play(false);
        Assert.Single(_played);
    }

    [Fact]
    public void MissingAssetsAndAFailedBackendStayQuiet()
    {
        CoreModArt.AudioOverridePathProvider = _ => _asset + ".missing";
        Play(false);
        Play(true);
        Assert.Empty(_played);
        CoreModArt.AudioOverridePathProvider = _ => _asset;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => throw new InvalidOperationException("unavailable");
        Assert.Null(Record.Exception(() => Play(false)));
        Assert.Null(Record.Exception(() => Play(true)));
    }

    private static void Play(bool splash)
    {
        if (splash) DescentRoomSfx.PlaySplashOpen();
        else DescentRoomSfx.PlayFogEntry();
    }

    public void Dispose()
    {
        Head.DescentCountdown?.Dispose();
        Head.DescentCountdown = _oldFuse;
        CoreModArt.AudioOverridePathProvider = _oldResolver;
        CoreAudio.PlayOneShotProvider = _oldAudio;
        CoreSettings.Current.MasterVolume = _oldVolume;
        CoreSettings.Current.DescentCountdownAudio = _oldEnabled;
        File.Delete(_asset);
    }
}
