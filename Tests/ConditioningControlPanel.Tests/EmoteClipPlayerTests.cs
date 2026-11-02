using System;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The tube's SkiaSharp clip lane (EmoteClipPlayer): play once, hold the last frame, pause, a new
/// ready token per load, and clip URIs for an embedded or an absolute folder (WebP first, then GIF).
/// Driven here with avatar0's idle GIF (SKCodec reads both formats; Rain's WebP clips are covered in
/// RainTubeClipsTests).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EmoteClipPlayerTests
{
    private static readonly Uri Idle = new("pack://application:,,,/Resources/avatar0_emotes/idle.gif", UriKind.Absolute);
    private static readonly Uri Wink = new("pack://application:,,,/Resources/avatar0_emotes/wink.gif", UriKind.Absolute);

    private static EmoteClipPlayer.Decoded DecodePack(Uri uri)
        => EmoteClipPlayer.Decode(EmoteClipPlayer.ReadBytes(uri))!;

    [Fact]
    public void Decode_ReadsEveryFrameAndItsDelay()
    {
        using var d = DecodePack(Idle);
        Assert.Equal(120, d.Durations.Length);
        Assert.Equal(7200, System.Linq.Enumerable.Sum(d.Durations));
        Assert.Equal(192, d.Canvas.Width);
        Assert.Null(EmoteClipPlayer.Decode(null));
        Assert.Null(EmoteClipPlayer.Decode(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Player_PlaysOnce_HoldsTheLastFrame_AndPauses()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var img = new Image();
            var player = new EmoteClipPlayer(img) { ManualClock = true };
            long now = 1000;
            player.Now = () => now;
            int completed = 0;
            player.Completed += _ => completed++;
            player.LoadDecoded(Idle, DecodePack(Idle));

            Assert.True(player.IsReady);
            Assert.IsAssignableFrom<WriteableBitmap>(img.Source);
            var first = player.ReadyToken;

            now += 3000; player.Tick(now);
            Assert.Equal(50, player.FrameIndex);
            Assert.False(player.IsComplete);

            player.Pause();
            now += 10_000; player.Tick(now);
            Assert.False(player.IsComplete);   // paused: the clock does not run
            player.Play();

            now += 4300; player.Tick(now);
            Assert.True(player.IsComplete);
            Assert.Equal(1, completed);
            now += 5000; player.Tick(now);
            Assert.Equal(1, completed);        // once per load
            Assert.NotNull(img.Source);        // holds the last frame

            // A fresh load is a NEW token (the crossfade's "new frame ready" test).
            player.LoadDecoded(Wink, DecodePack(Wink));
            Assert.NotSame(first, player.ReadyToken);
            Assert.Equal(Wink, player.Source);

            player.Stop();
            Assert.Null(img.Source);
            Assert.Null(player.Source);
            Assert.False(player.IsReady);
        });

    [Fact]
    public void ClipUris_TakeAnEmbeddedOrAnAbsoluteFolder()
    {
        Assert.Equal("pack://application:,,,/Resources/rain_emotes/idle.webp",
            AvatarTubeWindow.EmoteClipUri("rain_emotes", "idle", ".webp").ToString());
        var abs = AvatarTubeWindow.EmoteClipUri(@"C:\packs\abc", "idle", ".webp");
        Assert.True(abs.IsFile);
        Assert.Equal(@"C:\packs\abc\idle.webp", abs.LocalPath);
        Assert.True(AvatarTubeWindow.IsWebpClip(abs));
        Assert.False(AvatarTubeWindow.IsWebpClip(AvatarTubeWindow.EmoteClipUri("avatar0_emotes", "idle", ".gif")));
        Assert.Null(EmoteClipPlayer.ReadBytes(new Uri(@"C:\no\such\clip.webp")));
    }
}
