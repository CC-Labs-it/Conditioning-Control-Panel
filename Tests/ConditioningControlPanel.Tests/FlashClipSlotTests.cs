using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

// Owner, 2026-10-19: "after a few rounds none [of the GIF flashes] animate". The clip player
// budget must pair every acquire with a release, and the clip files must outlive their draw.
public class FlashClipSlotTests
{
    [Fact]
    public void BudgetRefusesAtCapAndReleaseFreesTheSlot()
    {
        var budget = new ClipSlotBudget(8);
        var leases = Enumerable.Range(0, 8).Select(_ => budget.TryAcquire()).ToList();
        Assert.All(leases, l => Assert.NotNull(l));
        Assert.Equal(8, budget.Live);
        Assert.False(budget.CanAcquire);
        Assert.Null(budget.TryAcquire());

        Assert.True(leases[3]!.Release());
        Assert.False(leases[3]!.Release());   // a second release is a no-op, never a drift
        leases[3]!.Dispose();
        Assert.Equal(7, budget.Live);
        Assert.True(budget.CanAcquire);
        Assert.NotNull(budget.TryAcquire());
        Assert.Equal(8, budget.Live);
    }

    [Fact]
    public void FullBudgetEvictsTheOldestLeavingPlayerInsteadOfAStill()
    {
        var budget = new ClipSlotBudget(3);
        var evicted = new List<int>();
        var a = budget.TryAcquire(() => evicted.Add(0))!;
        var b = budget.TryAcquire(() => evicted.Add(1))!;
        var c = budget.TryAcquire(() => evicted.Add(2))!;
        Assert.Null(budget.TryAcquire());

        c.Evictable = true;
        b.Evictable = true;
        var d = budget.TryAcquire(null, out var victim);
        Assert.NotNull(d);
        Assert.Same(b, victim);               // oldest evictable, not the newest
        Assert.Equal(new[] { 1 }, evicted);
        Assert.Equal(3, budget.Live);
        Assert.False(b.Release());            // the owner's own dispose after eviction changes nothing
        Assert.Equal(3, budget.Live);
        Assert.False(a.Evictable);
    }

    [Fact]
    public void BubblesAndCascadeShareASubCapSoFlashesAlwaysHaveRoom()
    {
        var budget = new ClipSlotBudget(8, 5);
        var shared = Enumerable.Range(0, 5).Select(_ => budget.TryAcquire()).ToList();
        Assert.All(shared, l => Assert.NotNull(l));
        Assert.False(budget.CanAcquireFor(reserved: false));
        Assert.Null(budget.TryAcquire());                 // a sixth bubble face is refused
        Assert.True(budget.CanAcquireFor(reserved: true));
        var flashes = Enumerable.Range(0, 3).Select(i => budget.TryAcquire(null, out _, reserved: true)).ToList();
        Assert.All(flashes, l => Assert.NotNull(l));
        Assert.Equal(8, budget.Live);
        Assert.Equal(5, budget.LiveShared);

        // A leaving flash may lend its slot to another flash, but a bubble over its sub-cap
        // only ever takes a slot from another shared lease.
        flashes[0]!.Evictable = true;
        Assert.Null(budget.TryAcquire());
        shared[2]!.Evictable = true;
        Assert.NotNull(budget.TryAcquire(null, out var victim));
        Assert.Same(shared[2], victim);
        Assert.NotNull(budget.TryAcquire(null, out victim, reserved: true));
        Assert.Same(flashes[0], victim);
        Assert.Equal(5, budget.LiveShared);
    }

    [Fact]
    public void EvictingAPlayerWhoseStartNeverFinishedDoesNotThrow()
    {
        // FlashClipPlayer hands the budget "created?.Dispose()" before the constructor returns.
        var budget = new ClipSlotBudget(1);
        object? created = null;
        var first = budget.TryAcquire(() => (created as IDisposable)?.Dispose())!;
        first.Evictable = true;
        Assert.NotNull(budget.TryAcquire());
        Assert.Equal(1, budget.Live);
    }

    [Fact]
    public void ReleaseOnExceptionPathKeepsTheCountExact()
    {
        var budget = new ClipSlotBudget(8);
        for (int i = 0; i < 100; i++)
        {
            var lease = budget.TryAcquire()!;
            try { throw new InvalidOperationException("Clip playback refused"); }
            catch (InvalidOperationException) { lease.Dispose(); }
        }
        Assert.Equal(0, budget.Live);
    }

    [Fact]
    public void ThousandRoundsOfFlashesNeverDriftTheCount()
    {
        var budget = new ClipSlotBudget(8);
        var rng = new Random(1019);
        var live = new List<ClipSlotBudget.Lease>();
        int evictions = 0;
        for (int round = 0; round < 1000; round++)
        {
            // 20 flashes a round, as in the owner's log; some close before their start completes.
            for (int f = 0; f < 20; f++)
            {
                ClipSlotBudget.Lease? self = null;
                var lease = budget.TryAcquire(() => { evictions++; live.Remove(self!); });
                self = lease;
                if (lease == null) continue;
                if (rng.Next(10) == 0) { lease.Dispose(); continue; }   // closed early
                live.Add(lease);
                if (rng.Next(4) == 0) lease.Evictable = true;            // fading out
            }
            Assert.True(budget.Live <= budget.Capacity);
            Assert.Equal(live.Count, budget.Live);
            // Round end: every flash closes, some through two paths at once.
            foreach (var lease in live.ToList())
            {
                lease.Dispose();
                if (rng.Next(3) == 0) lease.Dispose();
            }
            live.Clear();
            Assert.Equal(0, budget.Live);
            Assert.True(budget.CanAcquire);
        }
        Assert.True(evictions > 0);
    }

    [Fact]
    public void ClipFilesDrawnJustNowSurviveManyMoreWarms()
    {
        var files = new RemoteClipFiles();
        long now = 0;
        var released = new HashSet<string>();
        for (int i = 0; i < 64; i++) foreach (var p in files.Put("u" + i, "p" + i, "poster", now)) released.Add(p);
        Assert.Empty(released);

        // A draw picks the oldest clip, then the next prefetch warms 40 more (the old sweep
        // deleted exactly that file before LibVLC opened it).
        now = 200_000;
        Assert.True(files.TryUse("u0", now, out var drawn, out var poster));
        Assert.Equal("p0", drawn);
        Assert.Equal("poster", poster);
        for (int i = 64; i < 104; i++) foreach (var p in files.Put("u" + i, "p" + i, "poster", now)) released.Add(p);

        Assert.DoesNotContain("p0", released);
        Assert.True(files.TryUse("u0", now, out _, out _));
        Assert.Equal(RemoteClipFiles.SoftCap, files.Count);
        Assert.Contains("p1", released);      // the unused old ones go first
    }

    [Fact]
    public void ClipFilesReplaceReleasesTheOldFileAndHardCapHolds()
    {
        var files = new RemoteClipFiles();
        Assert.Empty(files.Put("u", "a", "poster", 0));
        Assert.Equal(new[] { "a" }, files.Put("u", "b", "poster", 0));
        Assert.Empty(files.Put("u", "b", "poster", 0));

        // Everything recent: the set grows past the soft cap but never past the hard cap.
        var dropped = new List<string>();
        for (int i = 0; i < 300; i++) dropped.AddRange(files.Put("r" + i, "rp" + i, "poster", 1000));
        Assert.Equal(RemoteClipFiles.HardCap, files.Count);
        Assert.Equal(301 - RemoteClipFiles.HardCap, dropped.Count);
        Assert.Equal(RemoteClipFiles.HardCap, files.TakeAll().Count);
        Assert.Equal(0, files.Count);
    }

    [Fact]
    public void ThousandRoundsKeepEveryDrawnClipOnDisk()
    {
        var files = new RemoteClipFiles();
        var onDisk = new HashSet<string>();
        var rng = new Random(7);
        long now = 0;
        int next = 0;
        for (int round = 0; round < 1000; round++)
        {
            now += 18_000;   // a round every ~18 s
            for (int w = 0; w < 30; w++)
            {
                var path = "p" + next;
                onDisk.Add(path);
                foreach (var gone in files.Put("u" + next, path, "poster", now)) onDisk.Remove(gone);
                next++;
            }
            // 20 flashes draw from the newest 24 plus reused older ones; each must still exist.
            for (int f = 0; f < 20; f++)
            {
                var url = "u" + (next - 1 - rng.Next(Math.Min(next, 60)));
                if (files.TryUse(url, now, out var path, out _)) Assert.Contains(path, onDisk);
            }
            Assert.True(files.Count <= RemoteClipFiles.HardCap);
            Assert.Equal(files.Count, onDisk.Count);
        }
    }

    // ---- source pins ------------------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Src(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Method(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing " + signature);
        int open = src.IndexOf('{', start), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("unbalanced " + signature);
    }

    [Fact]
    public void PlayerCountsLeasesAndReleasesOnEveryPath()
    {
        var src = Src("Services/Media/FlashClipPlayer.cs");
        Assert.DoesNotContain("_alive", src);
        Assert.Contains("Budget.TryAcquire(", src);
        var start = Method(src, "internal static FlashClipPlayer? TryStart(");
        Assert.Contains("lease.Dispose();", start.Substring(start.IndexOf("catch", StringComparison.Ordinal)));
        var dispose = Method(src, "public void Dispose()");
        Assert.True(dispose.IndexOf("_lease.Release()", StringComparison.Ordinal) < dispose.IndexOf("Task.Run", StringComparison.Ordinal),
            "the slot must go back before the deferred native teardown, so a slow Stop cannot hold it");
        Assert.Contains("VideoDiag.Log(\"CLIP\"", src);
        // A deleted temp file must not take a slot and show nothing.
        Assert.True(start.IndexOf("File.Exists(path)", StringComparison.Ordinal) < start.IndexOf("Budget.TryAcquire(", StringComparison.Ordinal));
        Assert.Contains("new(MaxPlayers, MaxSharedPlayers)", src);
        // Backward compatible entry point (bubbles, the GIF cascade).
        Assert.Contains("internal static FlashClipPlayer? Start(string? path, int width, int height, Action<BitmapSource> show, double speed = 1)", src);
    }

    [Theory]
    [InlineData("private void CloseAllWindows()")]
    [InlineData("private void CloseOneShotWindows()")]
    [InlineData("private void OnFlashClicked(")]
    public void EveryFlashClosePathGoesThroughSafeClose(string signature)
    {
        var body = Method(Src("Services/Flash/FlashService.cs"), signature);
        Assert.Contains("SafeCloseFlashWindow(", body);
    }

    [Fact]
    public void SafeCloseDisposesTheClipAndHeartbeatSweepUsesIt()
    {
        var src = Src("Services/Flash/FlashService.cs");
        var close = Method(src, "private void SafeCloseFlashWindow(FlashWindow window)");
        int dispose = close.IndexOf("window.ClipPlayer?.Dispose();", StringComparison.Ordinal);
        Assert.True(dispose >= 0);
        // Before the layer / host early returns, so no branch skips it.
        Assert.True(dispose < close.IndexOf("return;", StringComparison.Ordinal));
        var fade = Method(src, "private void GentleFadeThenClose(");
        Assert.True(Regex.Matches(fade, @"SafeCloseFlashWindow\(window\)").Count >= 3);
        Assert.Contains("foreach (var window in toRemove)", src);
        // Players are only ever created by the heartbeat, which checks the window is showing.
        Assert.Single(Regex.Matches(src, @"window\.ClipPlayer = FlashClipPlayer\.").Cast<Match>());
        Assert.Contains("FlashClipPlayer.CanStartFlash", src);
        Assert.Contains("out var atCap, flash: true)", src);
        Assert.Contains("Evictable = !showThisWindow", src);
    }

    [Fact]
    public void FlashClipFilesAreOwnedNotSweptByTheSharedTempLedger()
    {
        var src = Src("Services/Flash/FlashService.cs");
        Assert.Contains("MaterializeAsync(url, ct, ownerReleases: true)", src);
        Assert.Contains("RemoteClipFiles RemoteClips", src);
        Assert.Contains("RemoteClips.TakeAll()", Method(src, "public void Dispose()"));
        Assert.DoesNotContain("RemoteClips.Keys.Take(", src);
        // A decode-cache hit for a remote URL hands out the clip file held NOW, not the cached one.
        Assert.Contains("if (IsRemotePath(path)) hit.ClipPath = CurrentRemoteClipPath(path);", src);
        // A URL handed out again keeps its file instead of materializing a second copy.
        Assert.Contains("var path = held ?? await RemoteMediaCache.MaterializeAsync(url, ct, ownerReleases: true)", src);
    }

    [Fact]
    public void BubbleFaceClipIsDisposedOnDestroy()
    {
        var body = Method(Src("Services/BubbleService.cs"), "private void Destroy()");
        Assert.Contains("_faceClip?.Dispose();", body);
    }
}
