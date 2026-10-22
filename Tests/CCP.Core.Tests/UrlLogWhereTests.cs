using System;
using System.IO;
using ConditioningControlPanel.Services.Logging;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Main f07e12edd: a corner-GIF log line names a host, or for a file only its extension -
/// never the path or a Scrolller url (whose path carries the item id).</summary>
public sealed class UrlLogWhereTests
{
    [Fact]
    public void AFileSaysOnlyItsExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), "secret-folder", "my clip.gif");
        Assert.Equal("file .gif", UrlLog.Where(path));
        Assert.Equal("file .gif", UrlLog.Where(new Uri(path)));
    }

    [Fact]
    public void ARemoteSourceSaysOnlyItsHost()
    {
        Assert.Equal("cdn.scrolller.com", UrlLog.Where("https://cdn.scrolller.com/abc123/post-9876.gif?t=1"));
        Assert.Equal("cdn.scrolller.com", UrlLog.Where(new Uri("https://cdn.scrolller.com/abc123/post-9876.gif")));
    }

    [Fact]
    public void NothingOrJunkNeverEchoesTheInput()
    {
        Assert.Equal(UrlLog.Empty, UrlLog.Where((string?)null));
        Assert.Equal(UrlLog.Empty, UrlLog.Where((Uri?)null));
        Assert.Equal(UrlLog.Invalid, UrlLog.Where("relative/secret.gif"));
    }
}
