using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// PackFolderMover moves user data (installed, paid-for packs): copy -> verify -> remove, and the
/// source survives any failure. Packs are written with the real PackEncryptionService layout.
/// </summary>
public sealed class PackFolderMoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packmove-" + Guid.NewGuid().ToString("N"));
    private string Old => Path.Combine(_root, "old");
    private string Default => Path.Combine(_root, "default");
    private string New => Path.Combine(_root, "new");

    public PackFolderMoverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static string[] Snapshot(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f) + ":" + Convert.ToBase64String(File.ReadAllBytes(f)))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Move_CopiesVerifiesRegisters_ThenRemovesTheSource()
    {
        var (g1, _) = ContentPackStoreTests.WritePack(Old, "p1", "One", ("a.png", "image"), ("b.mp4", "video"));
        var (g2, _) = ContentPackStoreTests.WritePack(Default, "p2", "Two", ("z.png", "image"));
        var before1 = Snapshot(Path.Combine(Old, ".packs", g1));
        var settings = new AppSettings();

        var found = PackFolderMover.Find(Old, Default, New);
        Assert.Equal(new[] { "One", "Two" }, found.Select(c => c.PackName).OrderBy(n => n));
        var (moved, registered) = PackFolderMover.Move(found, New, settings);

        Assert.Equal((2, 2), (moved, registered));
        Assert.Equal(before1, Snapshot(Path.Combine(New, ".packs", g1)));
        Assert.False(Directory.Exists(Path.Combine(Old, ".packs", g1)));
        Assert.False(Directory.Exists(Path.Combine(Default, ".packs", g2)));
        Assert.False(Directory.Exists(Path.Combine(New, PackFolderMover.StagingFolderName)));   // no empty staging left
        Assert.Equal(g1, settings.PackGuidMap["p1"]);
        Assert.Equal(g2, settings.PackGuidMap["p2"]);
        Assert.Contains("p1", settings.ActivePackIds);

        // The moved packs are what the store now sees.
        var store = new ContentPackStore(() => New, () => settings, () => { });
        Assert.Equal(2, store.GetAllActivePackImages().Count);
    }

    [Fact]
    public void Move_FailureMidCopy_KeepsTheSource_AndLeavesNoPartialDestination()
    {
        var (good, _) = ContentPackStoreTests.WritePack(Old, "p1", "One", ("a.png", "image"));
        var (bad, _) = ContentPackStoreTests.WritePack(Old, "p2", "Two", ("x.png", "image"), ("y.png", "image"));
        var badSource = Path.Combine(Old, ".packs", bad);
        var before = Snapshot(badSource);
        var settings = new AppSettings();
        var found = PackFolderMover.Find(Old, Default, New).OrderBy(c => c.PackName).ToList();   // One, then Two

        // Hold one of p2's content files exclusively so the copy fails after other files landed.
        var locked = Directory.GetFiles(Path.Combine(badSource, "content")).OrderBy(f => f).Last();
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => PackFolderMover.Move(found, New, settings));

        Assert.Equal(before, Snapshot(badSource));                                   // source intact
        Assert.False(Directory.Exists(Path.Combine(New, ".packs", bad)));            // no half pack to register
        Assert.False(Directory.Exists(Path.Combine(New, PackFolderMover.StagingFolderName, bad)));
        Assert.False(settings.PackGuidMap.ContainsKey("p2"));
        Assert.Equal(good, settings.PackGuidMap["p1"]);                              // earlier pack still moved

        // A retry once the file is free completes the move.
        var (moved, _) = PackFolderMover.Move(PackFolderMover.Find(Old, Default, New), New, settings);
        Assert.Equal(1, moved);
        Assert.Equal(before, Snapshot(Path.Combine(New, ".packs", bad)));
        Assert.False(Directory.Exists(badSource));
    }

    [Fact]
    public void Verify_RejectsAShortOrMissingCopy()
    {
        var (g, _) = ContentPackStoreTests.WritePack(Old, "p1", "One", ("a.png", "image"), ("b.png", "image"));
        var src = Path.Combine(Old, ".packs", g);
        var copy = Path.Combine(_root, "copy");
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(copy, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        PackFolderMover.Verify(src, copy);

        var one = Directory.GetFiles(Path.Combine(copy, "content")).First();
        File.WriteAllBytes(one, new byte[1]);
        Assert.Throws<IOException>(() => PackFolderMover.Verify(src, copy));
        File.Delete(one);
        Assert.Throws<IOException>(() => PackFolderMover.Verify(src, copy));
    }

    [Fact]
    public void SameFolder_IsANoOp_ThatKeepsTheData()
    {
        var (g, _) = ContentPackStoreTests.WritePack(New, "p1", "One", ("a.png", "image"));
        var folder = Path.Combine(New, ".packs", g);
        var before = Snapshot(folder);
        var settings = new AppSettings();

        Assert.Empty(PackFolderMover.Find(New, New, New));          // never offers the destination's own packs
        var (moved, registered) = PackFolderMover.Move(
            new[] { new PackFolderMover.Candidate(folder, "One", 0) }, New, settings);

        Assert.Equal((0, 1), (moved, registered));
        Assert.Equal(before, Snapshot(folder));
        Assert.Equal(g, settings.PackGuidMap["p1"]);
    }

    [Fact]
    public void ExistingDestination_IsSkipped_AndTheSourceKept()
    {
        var (g, _) = ContentPackStoreTests.WritePack(Old, "p1", "One", ("a.png", "image"));
        var src = Path.Combine(Old, ".packs", g);
        Directory.CreateDirectory(Path.Combine(New, ".packs", g));
        var before = Snapshot(src);

        var (moved, _) = PackFolderMover.Move(PackFolderMover.Find(Old, Default, New), New, new AppSettings());

        Assert.Equal(0, moved);
        Assert.Equal(before, Snapshot(src));
    }

    [Fact]
    public void FormatSize_MatchesWpf() =>
        Assert.Equal(new[] { "512 bytes", "1.5 KB", "2.0 MB", "1.0 GB" },
            new[] { 512L, 1536L, 2L * 1024 * 1024, 1024L * 1024 * 1024 }.Select(PackFolderMover.FormatSize));
}
