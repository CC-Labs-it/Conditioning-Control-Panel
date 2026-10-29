using System;
using System.IO;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The 24 h day limit on every Scrolller copy reads the DOWNLOAD time on every OS. On Linux .NET cannot set a creation
/// time (SetCreationTimeUtc moves the last-write time) and GetCreationTimeUtc reads the real birth, the moment the copy was
/// made, so a copy made long after its download used to read young and outlive the day. These files are built the way a
/// stamped Linux copy reads: born now, last write at the download.
/// </summary>
[Collection(SessionStatics.Name)]   // DtrhRemoteMedia's copy map is process-wide
public sealed class ScrolllerCopyAgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-copy-age-").FullName;

    public void Dispose()
    {
        DtrhRemoteMedia.SweepLocalCopies(_ => { }, all: true);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A copy as Linux reads it after a stamp: creation = now (its birth), last write = the download.</summary>
    private string LinuxStampedCopy(string name, TimeSpan downloadedAgo)
    {
        var path = Path.Combine(_dir, ScrolllerDiskCache.TempPrefix + name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        File.SetCreationTimeUtc(path, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - downloadedAgo);
        return path;
    }

    [Fact]
    public void The_temp_sweep_reads_the_download_time_not_the_copy_time()
    {
        var old = LinuxStampedCopy("old.jpg", TimeSpan.FromHours(30));
        var fresh = LinuxStampedCopy("fresh.jpg", TimeSpan.FromHours(2));
        var deleted = ScrolllerDiskCache.SweepTempCopies(new[] { _dir }, ScrolllerDiskCache.TempPrefix,
            ScrolllerDiskCache.MaxAge - ScrolllerDiskCache.SweepPeriod);
        Assert.Equal(1, deleted);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void A_dtrh_copy_past_the_day_leaves_by_its_download_time()
    {
        var old = LinuxStampedCopy("dtrh-old.jpg", TimeSpan.FromHours(30));
        var fresh = LinuxStampedCopy("dtrh-fresh.jpg", TimeSpan.FromHours(2));
        Assert.True(DtrhRemoteMedia.AddLocalCopyForTests("https://images.scrolller.com/a/old.jpg", old));
        Assert.True(DtrhRemoteMedia.AddLocalCopyForTests("https://images.scrolller.com/a/fresh.jpg", fresh));
        string? released = null;
        Assert.Equal(1, DtrhRemoteMedia.SweepLocalCopies(p => released = p));
        Assert.Equal(old, released);
    }

    [Fact]
    public void A_stamp_lands_in_both_times_and_reads_back()
    {
        var path = Path.Combine(_dir, "stamped.jpg");
        File.WriteAllBytes(path, new byte[] { 1 });
        var born = DateTime.UtcNow - TimeSpan.FromHours(23);
        ScrolllerDiskCache.StampBorn(path, born);
        Assert.Equal(born, File.GetLastWriteTimeUtc(path), TimeSpan.FromSeconds(2));
        Assert.Equal(born, File.GetCreationTimeUtc(path), TimeSpan.FromSeconds(2));   // Windows keeps both; Linux the write time
        Assert.False(ScrolllerDiskCache.CopyDue(path, t => DateTime.UtcNow - t >= TimeSpan.FromHours(24)));
        Assert.True(ScrolllerDiskCache.CopyDue(path, t => DateTime.UtcNow - t >= TimeSpan.FromHours(22)));
    }

    [Fact]
    public void A_creation_time_far_ahead_still_counts_as_the_clock_going_back()
    {
        // Either stamp may say due: a copy whose creation reads 72 h AHEAD (the clock went back) goes even though its
        // last write reads now (the ScrolllerHeadTests case).
        var path = Path.Combine(_dir, ScrolllerDiskCache.TempPrefix + "ahead.jpg");
        File.WriteAllBytes(path, new byte[] { 1 });
        File.SetCreationTimeUtc(path, DateTime.UtcNow + TimeSpan.FromHours(72));
        Assert.Equal(1, ScrolllerDiskCache.SweepTempCopies(new[] { _dir }, ScrolllerDiskCache.TempPrefix,
            ScrolllerDiskCache.MaxAge - ScrolllerDiskCache.SweepPeriod));
    }
}
