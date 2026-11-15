using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.BillboardServices;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BillboardBoardProviderTests
{
    private sealed class Feed(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor(42, 85, 170, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void DecoderReadsBgraAndRejectsOversizedStrip()
    {
        var decoded = BoardPng.Decode(Png(4, 2))!.Value;
        Assert.Equal(unchecked((int)0xff2a55aa), decoded.Pixels[0]);
        Assert.Null(BoardPng.Decode(Png(513, 1)));
    }

    [Fact]
    public async Task MarqueeLoadsBoardAndProviderAppliesVisibilityAndSeenRules()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-board-" + Guid.NewGuid().ToString("N"));
        var previous = BoardPicture.PngDecoder;
        try
        {
            BoardPicture.PngDecoder = BoardPng.Decode;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache = new BoardCacheService(dir, (_, _) => Task.FromResult<byte[]?>(Png(4, 2)));
            cache.Changed += (_, _) => ready.TrySetResult();
            var provider = new BoardProvider(cache, s => s);
            var body = "{\"message\":\"hello\",\"board\":{\"v\":4,\"aud\":\"patrons\",\"w\":4,\"h\":2}}";
            Assert.Equal("hello", await new V2AuthService(() => null, new Feed(body)).GetMarqueeMessageAsync(cache.OnMarquee));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var now = DateTime.UtcNow;
            var context = new BillboardContext(BillboardTier.Prime, now, now.ToLocalTime());
            var card = Assert.Single(provider.Current(context));
            Assert.Equal("board:4", card.Id);
            Assert.Equal(BillboardBadge.New, card.Badge);
            Assert.False(card.Snoozable);
            Assert.IsType<BoardArtData>(card.ArtData);
            Assert.Equal(BoardTileView.ArtKey, card.ArtKey);
            cache.MarkShown(4);
            Assert.Equal(BillboardBadge.None, Assert.Single(provider.Current(context)).Badge);
            Assert.Empty(provider.Current(new BillboardContext(BillboardTier.Free, now, now.ToLocalTime())));
            var expired = cache.Current!.Post with { UntilUtc = now.AddSeconds(-1) };
            Assert.Null(BoardProvider.Decide(BoardPicture.Solid(expired, 0), context, 0, s => s));
        }
        finally
        {
            BoardPicture.PngDecoder = previous;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
