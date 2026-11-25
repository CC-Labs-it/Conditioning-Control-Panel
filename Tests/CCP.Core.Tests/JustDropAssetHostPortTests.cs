using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

public class JustDropAssetHostPortTests
{
    [Fact]
    public void OpaqueUrlsResolveOnlyTheirWindowAndAllowlistedSource()
    {
        var host = new JustDropAssetMap();
        var entry = new JustDropAssetMap.Entry { Ext = ".JPG", Video = false, DiskPath = "/private/a.jpg" };
        var url = host.Take("/private/a.jpg", entry)!;
        Assert.Matches(@"^https://justdrop-assets\.ccp/[0-9a-f]{32}\.jpg$", url);
        Assert.DoesNotContain("private", url);
        Assert.Equal(url, host.Take("/PRIVATE/A.JPG", new() { Ext = ".png", Video = false }));
        Assert.NotEqual(url, host.Take("/other/a.jpg", entry));
        Assert.Same(entry, host.Resolve(url));
        Assert.Null(new JustDropAssetMap().Resolve(url));
        Assert.Null(host.Take("bad", new() { Ext = ".jpg", Video = true }));
        Assert.Null(host.Take("bad", new() { Ext = ".svg", Video = false }));
        Assert.Null(host.Take("", entry));
        Assert.Null(host.Resolve(url.Replace("https://", "http://")));
        Assert.Null(host.Resolve(url.Replace("justdrop-assets.ccp", "justdrop-assets.ccp.evil")));
        Assert.Null(host.Resolve(url.Replace("https://", "https://user@")));
        Assert.Null(host.Resolve(url.Replace(".ccp/", ".ccp:444/")));
        Assert.Null(host.Resolve(url.Replace(".jpg", ".png")));
        host.Clear();
        Assert.Null(host.Resolve(url));
        Assert.NotEqual(url, host.Take("/private/a.jpg", entry));
    }

    /// <summary>WPF JustDropAssetHost.cs:87-101: shuffled, capped, unservable sources skipped, one id per source.</summary>
    [Fact]
    public void ListCapsAndSkipsUnservableSources()
    {
        var sources = new List<(string, JustDropAssetMap.Entry)>
        {
            ("/a.mp4", new() { Ext = ".mp4", Video = true, DiskPath = "/a.mp4" }),
            ("/b.webm", new() { Ext = ".webm", Video = true, DiskPath = "/b.webm" }),
            ("/c.avi", new() { Ext = ".avi", Video = true, DiskPath = "/c.avi" }),
        };
        var host = new JustDropAssetMap(video => video ? new(sources) : new());
        Assert.Empty(host.List(false, 10));
        var all = host.List(true, 10);
        Assert.Equal(2, all.Count);
        Assert.All(all, u => Assert.Matches(@"^https://justdrop-assets\.ccp/[0-9a-f]{32}\.(mp4|webm)$", u));
        Assert.Single(host.List(true, 1));
        Assert.Equal(new HashSet<string>(all), new HashSet<string>(host.List(true, 10)));
    }

    /// <summary>WPF JustDropAssetHost.cs:167-237: OPTIONS 204, unknown 404, whole 200, ranged 206, bad range 416,
    /// CORS on every answer; another host is not ours (null).</summary>
    [Fact]
    public void AnswerServesWholeRangedAndRefusals()
    {
        var file = Path.Combine(Path.GetTempPath(), "jd-asset-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        try
        {
            var host = new JustDropAssetMap();
            var url = host.Take(file, new() { Ext = ".png", Video = false, DiskPath = file })!;
            Assert.Null(host.Answer("GET", "https://example.com/x.png", null));
            var options = host.Answer("OPTIONS", url, null)!.Value;
            Assert.Equal(204, options.Status);
            Assert.Contains("Access-Control-Allow-Headers: Range", options.Headers);
            Assert.Equal(404, host.Answer("GET", JustDropAssetMap.Origin + "/" + new string('a', 32) + ".png", null)!.Value.Status);

            var whole = host.Answer("GET", url, null)!.Value;
            using (whole.Body)
            {
                Assert.Equal(200, whole.Status);
                Assert.Contains("Content-Type: image/png", whole.Headers);
                Assert.Contains("Content-Length: 10", whole.Headers);
                Assert.Contains("Access-Control-Allow-Origin: https://app.cclabs.app", whole.Headers);
            }
            var part = host.Answer("GET", url, "bytes=2-4")!.Value;
            using (part.Body)
            {
                Assert.Equal(206, part.Status);
                Assert.Contains("Content-Range: bytes 2-4/10", part.Headers);
                var buf = new MemoryStream();
                part.Body!.CopyTo(buf);
                Assert.Equal(new byte[] { 3, 4, 5 }, buf.ToArray());
            }
            var bad = host.Answer("GET", url, "bytes=50-60")!.Value;
            Assert.Equal(416, bad.Status);
            Assert.Contains("Content-Range: bytes */10", bad.Headers);
        }
        finally { File.Delete(file); }
    }

    /// <summary>WPF JustDropAssetHost.cs:217-252: a pack entry decrypts once on its first request; Clear deletes the copy.</summary>
    [Fact]
    public void PackEntryDecryptsOnceAndClearDeletesTheCopy()
    {
        var temp = Path.Combine(Path.GetTempPath(), "jd-pack-" + Guid.NewGuid().ToString("N") + ".mp4");
        int decrypts = 0;
        var deleted = new List<string>();
        var host = new JustDropAssetMap(decrypt: _ => { decrypts++; File.WriteAllBytes(temp, new byte[4]); return temp; },
            deleteTemp: p => { deleted.Add(p); File.Delete(p); });
        try
        {
            var url = host.Take("pack:p1/x", new() { Ext = ".mp4", Video = true, PackId = "p1", PackFile = new PackFileEntry() })!;
            host.Answer("GET", url, null)!.Value.Body?.Dispose();
            host.Answer("GET", url, null)!.Value.Body?.Dispose();
            Assert.Equal(1, decrypts);
            host.Clear();
            Assert.Equal(new[] { temp }, deleted);
            Assert.False(File.Exists(temp));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
