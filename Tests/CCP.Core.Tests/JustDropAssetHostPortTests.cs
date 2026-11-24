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
}
