using System;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// BoardPicture.Decode's own rules around the head's PngDecoder: the signature check, no decoder,
/// a decoder that throws, a short pixel array and the strip size limit (FitsStrip). The decoder is
/// process-wide, so every test restores it (Core tests run serially).
/// </summary>
public class BoardPictureDecodeTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 };

    private static BoardPost Post() => new(3, Array.Empty<string>(), null, "lobby", BoardAudience.Everyone, 1, 6, 4, 2);

    private static BoardPicture? DecodeWith(Func<byte[], (int[] Pixels, int Width, int Height)?>? decoder, byte[]? png = null)
    {
        var saved = BoardPicture.PngDecoder;
        try
        {
            BoardPicture.PngDecoder = decoder;
            return BoardPicture.Decode(png ?? Png, Post());
        }
        finally
        {
            BoardPicture.PngDecoder = saved;
        }
    }

    private static (int[] Pixels, int Width, int Height)? Good(byte[] _) => (new int[4 * 2], 4, 2);

    [Fact]
    public void A_good_decode_lays_out_a_picture() => Assert.NotNull(DecodeWith(Good));

    [Fact]
    public void No_decoder_means_no_picture() => Assert.Null(DecodeWith(null));

    [Fact]
    public void Bytes_without_the_png_signature_never_reach_the_decoder()
    {
        bool called = false;
        Assert.Null(DecodeWith(b => { called = true; return Good(b); }, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0 }));
        Assert.False(called);
    }

    [Fact]
    public void A_decoder_that_throws_means_no_picture() =>
        Assert.Null(DecodeWith(_ => throw new InvalidOperationException("bad png")));

    [Fact]
    public void A_short_pixel_array_means_no_picture() =>
        Assert.Null(DecodeWith(_ => (new int[4 * 2 - 1], 4, 2)));

    [Fact]
    public void A_strip_past_the_limit_means_no_picture()
    {
        int maxW = BoardWire.MaxFrameWidth * BoardWire.MaxFrames;
        Assert.True(BoardPicture.FitsStrip(maxW, 512));
        Assert.False(BoardPicture.FitsStrip(maxW + 1, 512));
        Assert.False(BoardPicture.FitsStrip(maxW, 513));
        Assert.Null(DecodeWith(_ => (new int[(maxW + 1) * 2], maxW + 1, 2)));
        Assert.Null(DecodeWith(_ => (new int[4 * 513], 4, 513)));
    }
}
