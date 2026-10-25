using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DescentRoomAudioTests
{
    [Theory]
    [InlineData(DescentRoomCue.FogEntry, "descent_fog_enter", "ui_deepen", 0.21f, "descent-fog-enter")]
    [InlineData(DescentRoomCue.SplashOpen, "descent_spiral_open", "reveal_chime", 0.17f, "descent-spiral-open")]
    [InlineData(DescentRoomCue.Crack, "descent_crack", "descent_zero_flash", 0.275f, "descent-crack")]
    public void DedicatedCueWinsAndMissingCueFallsBack(DescentRoomCue cue, string first, string fallback, float volume, string tag)
    {
        var old = CoreAudio.PlayOneShotProvider;
        var probes = new List<string>();
        var played = new List<(string Path, float Volume, string Tag)>();
        CoreAudio.PlayOneShotProvider = (p, v, t, _, _) => played.Add((p, v, t));
        try
        {
            DescentRoomAudio.Play(cue, true, 50, n => { probes.Add(n); return n; });
            Assert.Equal(new[] { first }, probes);
            Assert.Equal((first, volume, tag), Assert.Single(played));
            probes.Clear(); played.Clear();
            DescentRoomAudio.Play(cue, true, 50, n => { probes.Add(n); return n == first ? "" : n; });
            Assert.Equal(new[] { first, fallback }, probes);
            Assert.Equal((fallback, volume, tag), Assert.Single(played));
        }
        finally { CoreAudio.PlayOneShotProvider = old; }
    }

    [Fact]
    public void CrackKeepsTheLegacySlotAndClampsInvalidMasterVolume()
    {
        var old = CoreAudio.PlayOneShotProvider;
        var probes = new List<string>();
        string? played = null;
        float volume = 0;
        CoreAudio.PlayOneShotProvider = (p, v, _, _, _) => { played = p; volume = v; };
        try
        {
            DescentRoomAudio.Play(DescentRoomCue.Crack, true, 500, n =>
            {
                probes.Add(n);
                return n == "freeze_trigger" ? n : "";
            });
            Assert.Equal(new[] { "descent_crack", "descent_zero_flash", "freeze_trigger" }, probes);
            Assert.Equal("freeze_trigger", played);
            Assert.Equal(1f, volume);
        }
        finally { CoreAudio.PlayOneShotProvider = old; }
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void SilenceGateDoesNotResolveOrPlay(bool enabled, int volume)
    {
        var probes = 0;
        DescentRoomAudio.Play(DescentRoomCue.FogEntry, enabled, volume, _ => { probes++; return "cue"; });
        Assert.Equal(0, probes);
    }

    [Fact]
    public void MissingAndBrokenAssetsStaySilent()
    {
        var old = CoreAudio.PlayOneShotProvider;
        var played = 0;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => played++;
        try
        {
            DescentRoomAudio.Play(DescentRoomCue.FogEntry, true, 100, _ => "");
            DescentRoomAudio.Play(DescentRoomCue.FogEntry, true, 100, _ => throw new Exception("broken resolver"));
            Assert.Equal(0, played);
        }
        finally { CoreAudio.PlayOneShotProvider = old; }
    }
}
