using System;
using System.IO;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// DtRH's online pool under the licence day limit (owner, 2026-10-21): a remote entry lives 24 h
/// (was 3 days), and a local copy of its bytes whose file is a day old leaves the map and is
/// deleted before it can be served again.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]   // the off switch sweeps the same copies
public sealed class DtrhRemoteEntryTtlTests
{
    private const long Now = 1_790_000_000;

    private static DtrhRemoteMedia.RemoteEntry At(long atUnix)
        => new("scrolller/sub/1", "https://images.scrolller.com/a.jpg", true, atUnix);

    [Fact]
    public void The_ttl_is_a_day()
        => Assert.Equal(TimeSpan.FromHours(24), DtrhAssetManifest.RemoteEntryTtl);

    [Theory]
    [InlineData(23 * 3600, true)]
    [InlineData(24 * 3600 - 1, true)]
    [InlineData(24 * 3600, false)]
    [InlineData(3 * 24 * 3600, false)]
    [InlineData(0, true)]
    public void Fresh_until_24_hours(long age, bool fresh)
        => Assert.Equal(fresh, DtrhAssetManifest.RemoteEntryFresh(At(Now - age), Now));

    [Fact]
    public void No_time_or_a_time_far_ahead_is_stale()
    {
        Assert.False(DtrhAssetManifest.RemoteEntryFresh(At(0), Now));
        Assert.False(DtrhAssetManifest.RemoteEntryFresh(At(Now + 2 * 3600), Now));
        Assert.True(DtrhAssetManifest.RemoteEntryFresh(At(Now + 600), Now));
    }

    [Fact]
    public void A_copy_a_day_old_is_dropped_and_deleted_by_the_sweep()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-dtrh-ttl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var oldUrl = "https://images.scrolller.com/dtrh-old-" + Guid.NewGuid().ToString("N") + ".jpg";
            var newUrl = "https://images.scrolller.com/dtrh-new-" + Guid.NewGuid().ToString("N") + ".jpg";
            var oldFile = Path.Combine(dir, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + ".jpg");
            var newFile = Path.Combine(dir, "ccp_temp_remote_" + Guid.NewGuid().ToString("N") + ".jpg");
            File.WriteAllText(oldFile, "x");
            File.WriteAllText(newFile, "x");
            File.SetCreationTimeUtc(oldFile, DateTime.UtcNow.AddHours(-25));
            File.SetCreationTimeUtc(newFile, DateTime.UtcNow.AddHours(-1));
            Assert.True(DtrhAssetManifest.AddLocalCopyForTests(oldUrl, oldFile));
            Assert.True(DtrhAssetManifest.AddLocalCopyForTests(newUrl, newFile));

            Assert.True(DtrhAssetManifest.SweepLocalCopies() >= 1);
            Assert.False(DtrhAssetManifest.HasLocalCopyForTests(oldUrl));
            Assert.False(File.Exists(oldFile));
            Assert.True(DtrhAssetManifest.HasLocalCopyForTests(newUrl));
            Assert.True(File.Exists(newFile));

            DtrhAssetManifest.SweepLocalCopies(all: true);   // the off switch: every copy goes
            Assert.False(DtrhAssetManifest.HasLocalCopyForTests(newUrl));
            Assert.False(File.Exists(newFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
