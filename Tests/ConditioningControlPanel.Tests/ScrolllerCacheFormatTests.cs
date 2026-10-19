using System;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Animated online media stays animated in the device cache (owner, 2026-10-19: "most gifs stored
/// are converted to plain still images"). The provider serves a GIF post as a silent webm/mp4 plus
/// a static poster; the clip consumers take the video rendition, the cache keeps the bytes exactly
/// as served, a body whose bytes are not what its url and label say (an S3 error page under a .mp4
/// url, a still under a gif label) is never kept and an old one misses once, and no image encoder
/// ever touches cached online media.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerCacheFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-scache-fmt-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerCacheFormatTests()
    {
        Restart();
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.UtcNow = () => DateTime.UtcNow;
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Restart(bool check = true)
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        ScrolllerDiskCache.UtcNow = () => _now;
        ScrolllerDiskCache.FormatCheck = check;
    }

    // ---- sample heads ---------------------------------------------------------------------

    private static byte[] Pad(byte[] head)
    {
        var b = new byte[96];
        Array.Copy(head, b, Math.Min(head.Length, b.Length));
        return b;
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Mp4() => Pad(new byte[] { 0, 0, 0, 0x20 }.Concat(Ascii("ftypisom")).ToArray());
    private static byte[] Webm() => Pad(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.Concat(Ascii("....webm")).ToArray());
    private static byte[] Gif() => Pad(Ascii("GIF89a"));
    private static byte[] Png() => Pad(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    private static byte[] Jpeg() => Pad(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
    private static byte[] StillWebp() => Pad(Ascii("RIFF\0\0\0\0WEBPVP8 "));
    private static byte[] AnimatedWebp()
    {
        var b = Pad(Ascii("RIFF\0\0\0\0WEBPVP8X"));
        b[20] = 0x02;   // VP8X flags: animation
        return b;
    }
    private static byte[] S3Error() => Ascii("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Error><Code>AccessDenied</Code><Message>Access Denied</Message></Error>");

    // ---- magic-byte sniff -----------------------------------------------------------------

    [Fact]
    public void Sniff_names_each_format_by_its_bytes_and_an_error_page_as_nothing()
    {
        Assert.Equal(".gif", ScrolllerDiskCache.SniffFormat(Gif(), 64));
        Assert.Equal(".png", ScrolllerDiskCache.SniffFormat(Png(), 64));
        Assert.Equal(".jpg", ScrolllerDiskCache.SniffFormat(Jpeg(), 64));
        Assert.Equal(".webp", ScrolllerDiskCache.SniffFormat(StillWebp(), 64));
        Assert.Equal(".mp4", ScrolllerDiskCache.SniffFormat(Mp4(), 64));
        Assert.Equal(".webm", ScrolllerDiskCache.SniffFormat(Webm(), 64));
        Assert.Null(ScrolllerDiskCache.SniffFormat(S3Error(), 64));
        Assert.Null(ScrolllerDiskCache.SniffFormat(new byte[64], 0));
    }

    [Fact]
    public void Animated_webp_is_told_from_a_still_one()
    {
        Assert.True(ScrolllerDiskCache.IsAnimatedWebp(AnimatedWebp(), 64));
        Assert.False(ScrolllerDiskCache.IsAnimatedWebp(StillWebp(), 64));
        Assert.False(ScrolllerDiskCache.IsAnimatedWebp(Gif(), 64));
    }

    public static TheoryData<string, string, string, bool> Fits => new()
    {
        { ".mp4", "gif", "mp4", true },
        { ".webm", "gif", "webm", true },
        { ".gif", "gif", "gif", true },
        { ".webp", "gif", "awebp", true },
        { ".webp", "gif", "webp", false },     // a still under a gif label
        { ".jpg", "gif", "jpeg", false },
        { ".mp4", "gif", "s3", false },        // the AccessDenied body seen in the real cache
        { ".mp4", "clip", "webp", false },
        { ".webm", "clip", "webm", true },
        { ".webp", "poster", "webp", true },
        { ".jpg", "picture", "jpeg", true },
        { ".webp", "poster", "mp4", false },
        { ".bin", "picture", "png", true },
        { ".bin", "picture", "s3", false },
    };

    [Theory]
    [MemberData(nameof(Fits))]
    public void A_body_is_kept_only_when_its_bytes_match_its_url_and_label(string ext, string kind, string body, bool fits)
    {
        var head = body switch
        {
            "mp4" => Mp4(), "webm" => Webm(), "gif" => Gif(), "awebp" => AnimatedWebp(), "webp" => StillWebp(),
            "jpeg" => Jpeg(), "png" => Png(), _ => S3Error(),
        };
        Assert.Equal(fits, ScrolllerDiskCache.BodyFits(ext, kind, head, Math.Min(64, head.Length)));
    }

    // ---- the cache on disk ----------------------------------------------------------------

    private bool Put(string url, string kind, byte[] body)
    {
        using var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta(kind, "sub"));
        Assert.NotNull(w);
        w!.Stream.Write(body, 0, body.Length);
        w.Commit();
        _now = _now.AddMinutes(1);
        return ScrolllerDiskCache.Contains(url);
    }

    [Fact]
    public void An_error_page_under_a_clip_url_is_never_cached_and_a_real_clip_is_kept_byte_for_byte()
    {
        Assert.False(Put("https://helios.scrolller.com/Bad-mobile.mp4", "gif", S3Error()));
        Assert.True(Put("https://helios.scrolller.com/Good-mobile.mp4", "gif", Mp4()));
        var path = ScrolllerDiskCache.TryGetPath("https://helios.scrolller.com/Good-mobile.mp4")!;
        Assert.EndsWith(".mp4", path);
        Assert.Equal(Mp4(), File.ReadAllBytes(path));   // stored as served, no re-encode
        Assert.False(Directory.GetFiles(_root).Any(f => f.EndsWith(".part")));
    }

    [Fact]
    public void Old_misfits_miss_once_at_load_and_the_rest_of_the_cache_stays()
    {
        Restart(check: false);   // what a build before the check kept
        var stillUnderGif = "https://images.scrolller.com/a/still-under-gif.webp";
        var errorUnderClip = "https://helios.scrolller.com/Err-mobile.mp4";
        var clip = "https://quark.scrolller.com/fine.webm";
        var poster = "https://images.scrolller.com/a/fine-540x960.webp";
        Assert.True(Put(stillUnderGif, "gif", StillWebp()));
        Assert.True(Put(errorUnderClip, "gif", S3Error()));
        Assert.True(Put(clip, "gif", Webm()));
        Assert.True(Put(poster, "poster", StillWebp()));
        var stale = ScrolllerDiskCache.TryGetPath(errorUnderClip)!;
        ScrolllerDiskCache.Flush();

        Restart(check: true);
        Assert.False(ScrolllerDiskCache.Contains(stillUnderGif));
        Assert.False(ScrolllerDiskCache.Contains(errorUnderClip));
        Assert.True(ScrolllerDiskCache.Contains(clip));
        Assert.True(ScrolllerDiskCache.Contains(poster));
        Assert.False(File.Exists(stale));   // swept as a stranger, so the next ask fetches it again
    }

    // ---- rendition choice -----------------------------------------------------------------

    [Fact]
    public void A_gif_post_gives_clip_consumers_an_animated_rendition_at_both_sizes()
    {
        var sources = new JArray
        {
            new JObject { ["url"] = "https://i.scrolller.com/g1-1080.webp", ["width"] = 1080, ["height"] = 1920 },
            new JObject { ["url"] = "https://i.scrolller.com/g1-540.webp", ["width"] = 540, ["height"] = 960 },
            new JObject { ["url"] = "https://v.scrolller.com/g1_thumb.webm", ["width"] = 240, ["height"] = 426 },
            new JObject { ["url"] = "https://v.scrolller.com/g1.webm", ["width"] = 720, ["height"] = 1280 },
            new JObject { ["url"] = "https://v.scrolller.com/g1-mobile.mp4", ["width"] = 360, ["height"] = 640 },
        };
        var item = new JObject { ["id"] = 7001, ["title"] = "g1", ["mediaSources"] = sources };
        var e = ScrolllerSource.MapItem(item, "sub", stills: false, filter: "GIF");
        Assert.NotNull(e);
        Assert.Equal("video", e!.Type);
        Assert.True(RemoteMediaFormats.IsRemoteVideo(e.Url));
        Assert.True(e.SmallUrl == null || RemoteMediaFormats.IsRemoteVideo(e.SmallUrl));
        Assert.DoesNotContain("_thumb", e.Url);
        Assert.Equal("gif", ScrolllerItemIndex.MetaFor(e.Url)!.Kind);
        Assert.True(RemoteMediaFormats.IsRemoteImage(e.PosterUrl));   // the still is only the poster
    }

    // ---- source pins ----------------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void No_image_encoder_touches_cached_online_media()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Fyp", "Online");
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.Contains(files, f => f.EndsWith("ScrolllerDiskCache.cs"));
        Assert.Contains(files, f => f.EndsWith("ScrolllerWebGate.cs"));
        Assert.Contains(files, f => f.EndsWith("RemoteMediaCache.cs"));
        foreach (var f in files)
        {
            var src = File.ReadAllText(f);
            Assert.DoesNotContain("BitmapEncoder", src);       // Png/Jpeg/Gif/Bmp/Tiff/Wmp encoders
            Assert.DoesNotContain("BitmapFrame.Create", src);
            Assert.DoesNotContain("SKImage.FromBitmap", src);
            Assert.DoesNotContain(".Encode(", src);
        }
    }

    [Fact]
    public void The_gif_cascade_plays_a_remote_gif_as_its_clip_not_its_poster()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Chaos", "ChaosGifCascadeOverlay.cs"));
        Assert.Contains("FlashService.LoadRemoteFaceAsync(url, size, true)", src);
        Assert.Contains("FlashClipPlayer.Start(data.ClipPath", src);
        Assert.Contains("f.Clip?.Dispose()", src);
    }
}
