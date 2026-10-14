using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The two seams the Race rules gained moving to Core. WPF seeds both at startup
/// (App.xaml.cs, next to CoreSettings.ServiceProvider): RacingAccess asks PrizeGrants.IsGranted,
/// TrackDecoder runs NAudioTrackDecode. Unseeded must fail shut, never open.
/// </summary>
public class RaceCoreSeamsTests
{
    [Fact]
    public void RacingDoorAsksTheSeededPrizeStore()
    {
        var saved = RacingAccess.IsGrantedProvider;
        try
        {
            RacingAccess.IsGrantedProvider = null;
            Assert.False(RacingAccess.CanLaunch);
            Assert.Empty(RacingAccess.OwnedTracks);

            RacingAccess.IsGrantedProvider = id => id == "rt.original.03";
            Assert.True(RacingAccess.CanLaunch);
            Assert.Equal(new[] { 3 }, RacingAccess.OwnedTracks);

            RacingAccess.IsGrantedProvider = _ => throw new InvalidOperationException("store down");
            Assert.False(RacingAccess.CanLaunch);
        }
        finally { RacingAccess.IsGrantedProvider = saved; }
    }

    [Fact]
    public void RacingTrackIdsMatchPrizeGrants()
    {
        // WPF PrizeGrants.RacingTrack(n): "rt.original." + two digits, 0..10.
        Assert.Equal("rt.original.00", RacingAccess.RacingTrack(0));
        Assert.Equal("rt.original.10", RacingAccess.RacingTrack(RacingAccess.RacingTrackMax));
        Assert.Equal(10, RacingAccess.RacingTrackMax);
    }

    [Fact]
    public void DecoderRunsTheSeededHeadDecodeAndKeysTheFile()
    {
        var saved = TrackDecoder.DecodeProvider;
        var file = Path.Combine(Path.GetTempPath(), "race-seam-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });
        try
        {
            TrackDecoder.DecodeProvider = null;
            Assert.Throws<InvalidOperationException>(() => TrackDecoder.Decode(file, null, CancellationToken.None));

            string? asked = null;
            TrackDecoder.DecodeProvider = (p, _, _) => { asked = p; return new float[TrackPcm.SampleRate * 2]; };
            var pcm = TrackDecoder.Decode(file, null, CancellationToken.None);
            Assert.Equal(file, asked);
            Assert.Equal(2.0, pcm.DurationSec, 6);
            Assert.Equal(Path.GetFileName(file), pcm.Name);
            Assert.Equal(TrackDecoder.HashFile(file), pcm.Hash);
            Assert.Equal(40, pcm.Hash.Length);
        }
        finally
        {
            TrackDecoder.DecodeProvider = saved;
            File.Delete(file);
        }
    }
}
