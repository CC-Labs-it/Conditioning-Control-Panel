using System;
using System.IO;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The <c>ccp_temp_remote_</c> files under the licence day limit (owner, 2026-10-21): the hourly
/// sweep deletes every one whose creation time (its download time) is old enough, never another
/// app file, and the off switch (age zero) deletes them all.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class RemoteMediaCacheTempSweepTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-temp-sweep-" + Guid.NewGuid().ToString("N"));

    public RemoteMediaCacheTempSweepTests()
    {
        Directory.CreateDirectory(_dir);
        RemoteMediaCache.TempDirProvider = () => _dir;
    }

    public void Dispose()
    {
        RemoteMediaCache.TempDirProvider = () => null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Make(string name, double hoursOld)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddHours(-hoursOld));
        return path;
    }

    [Fact]
    public void A_file_25_hours_old_goes_and_a_1_hour_one_stays()
    {
        var old = Make("ccp_temp_remote_old.jpg", 25);
        var young = Make("ccp_temp_remote_young.jpg", 1);
        Assert.Equal(1, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.FromHours(24)));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(young));
    }

    [Fact]
    public void Only_our_remote_temps_are_touched()
    {
        var pack = Make("ccp_temp_pack_clip.mp4", 48);
        var user = Make("holiday.jpg", 48);
        Assert.Equal(0, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.FromHours(24)));
        Assert.True(File.Exists(pack));
        Assert.True(File.Exists(user));
    }

    [Fact]
    public void Age_zero_deletes_every_remote_temp()
    {
        Make("ccp_temp_remote_a.jpg", 0.01);
        Make("ccp_temp_remote_b.mp4", 3);
        Assert.Equal(2, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.Zero));
        Assert.Empty(Directory.GetFiles(_dir, "ccp_temp_remote_*"));
    }

    [Fact]
    public void The_off_switch_deletes_every_remote_temp()
    {
        Make("ccp_temp_remote_c.jpg", 0.5);
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        Assert.Empty(Directory.GetFiles(_dir, "ccp_temp_remote_*"));
    }

    [Fact]
    public void No_folder_is_a_no_op()
    {
        RemoteMediaCache.TempDirProvider = () => null;
        Assert.Equal(0, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.Zero));
    }

    [Fact]
    public void The_system_temp_fallback_folder_is_swept_too()
    {
        var sys = _dir + "-sys";
        Directory.CreateDirectory(sys);
        try
        {
            RemoteMediaCache.SystemTempDirProvider = () => sys;
            var path = Path.Combine(sys, "ccp_temp_remote_fallback.jpg");
            File.WriteAllText(path, "x");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddHours(-25));
            var young = Make("ccp_temp_remote_young2.jpg", 1);
            Assert.Equal(1, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.FromHours(23)));
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(young));
        }
        finally
        {
            RemoteMediaCache.SystemTempDirProvider = () => null;
            try { Directory.Delete(sys, true); } catch { }
        }
    }

    [Fact]
    public void The_same_folder_twice_is_walked_once()
    {
        RemoteMediaCache.SystemTempDirProvider = () => _dir + Path.DirectorySeparatorChar;
        try
        {
            Make("ccp_temp_remote_once.jpg", 30);
            Assert.Equal(1, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.FromHours(23)));
        }
        finally { RemoteMediaCache.SystemTempDirProvider = () => null; }
    }

    [Fact]
    public void A_copy_stamped_days_ahead_goes_at_once()
    {
        // The clock went back after the download (or was wrong when it was made): the copy's
        // creation time sits three days in the future. It used to read as "-72 h old" and stay.
        var ahead = Make("ccp_temp_remote_ahead.jpg", -72);
        var young = Make("ccp_temp_remote_young3.jpg", 1);
        Assert.Equal(1, RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.FromHours(23)));
        Assert.False(File.Exists(ahead));
        Assert.True(File.Exists(young));
    }

    [Theory]
    [InlineData(25.0, 24.0, true)]
    [InlineData(24.0, 24.0, true)]
    [InlineData(23.5, 24.0, false)]
    [InlineData(1.0, 23.0, false)]
    [InlineData(0.0, 0.0, true)]      // the off switch: every copy
    [InlineData(-0.5, 23.0, false)]   // half an hour ahead: clock jitter, kept
    [InlineData(-2.0, 23.0, true)]    // more than an hour ahead: the clock went back
    [InlineData(-72.0, 23.0, true)]
    public void Temp_copy_due_matrix(double hoursOld, double ageHours, bool due)
    {
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(due, RemoteMediaCache.TempCopyDue(now.AddHours(-hoursOld), now, TimeSpan.FromHours(ageHours)));
    }
}

/// <summary>
/// A temp copy is born at its bytes' download time. When the memory row is gone (evicted by a
/// burst of stores), the device cache's row gives it; a Scrolller url neither knows is taken as
/// old as a copy may be, so it never outlives the day limit by a second day.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class RemoteMediaCacheDownloadTimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-dltime-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public RemoteMediaCacheDownloadTimeTests()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        ScrolllerDiskCache.UtcNow = () => _now;
        RemoteMediaCache.UtcNow = () => _now;
        RemoteMediaCache.ClearMemory();
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => null;
        ScrolllerDiskCache.UtcNow = () => DateTime.UtcNow;
        RemoteMediaCache.UtcNow = () => DateTime.UtcNow;
        RemoteMediaCache.ClearMemory();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void The_memory_row_gives_the_time()
    {
        var url = "https://images.scrolller.com/dl/mem.jpg";
        RemoteMediaCache.Store(url, new byte[] { 1 }, _now.AddHours(-20));
        Assert.Equal(_now.AddHours(-20), RemoteMediaCache.DownloadTimeOf(url));
    }

    [Fact]
    public void With_the_memory_row_evicted_the_disk_row_gives_it()
    {
        var url = "https://images.scrolller.com/dl/disk.jpg";
        var downloaded = _now;
        using (var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture"))!)
        {
            w.Stream.Write(new byte[8], 0, 8);
            w.Commit();
        }
        _now = _now.AddHours(22);
        RemoteMediaCache.ClearMemory();
        Assert.Equal(downloaded, RemoteMediaCache.DownloadTimeOf(url));
    }

    [Fact]
    public void A_Scrolller_url_nobody_knows_is_as_old_as_a_copy_may_be()
    {
        var at = RemoteMediaCache.DownloadTimeOf("https://images.scrolller.com/dl/unknown.jpg");
        Assert.Equal(_now - ScrolllerDiskCache.MaxAge, at);
    }

    [Fact]
    public void Any_other_url_has_no_day_limit()
    {
        Assert.Null(RemoteMediaCache.DownloadTimeOf("https://example.org/a.jpg"));
    }
}
