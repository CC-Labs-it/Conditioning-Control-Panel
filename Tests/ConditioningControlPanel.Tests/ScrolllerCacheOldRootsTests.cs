using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The cache folder moves with the assets folder (a new CustomAssetsPath, or a custom drive that
/// fell back to the default). A folder left behind is never read again, so the day limit and the
/// off switch must still reach it: its media and its remote temp copies go at the move, on every
/// sweep (remembered folders and the default/custom seeds from builds before the list), and on
/// the off switch. Runs with the switch tests: the switch is process-wide.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerCacheOldRootsTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "ccp-scache-roots-" + Guid.NewGuid().ToString("N"));
    private readonly string _rootA;
    private readonly string _rootB;
    private readonly string _rootsFile;
    private string? _current;
    private string[] _seeds = Array.Empty<string>();
    private readonly DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerCacheOldRootsTests()
    {
        _rootA = Path.Combine(_base, "assets-a", ScrolllerDiskCache.FolderName);
        _rootB = Path.Combine(_base, "assets-b", ScrolllerDiskCache.FolderName);
        _rootsFile = Path.Combine(_base, "userdata", ScrolllerDiskCache.RootsFileName);
        _current = _rootA;
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _current;
        ScrolllerDiskCache.RootsFileProvider = () => _rootsFile;
        ScrolllerDiskCache.SeedRootsProvider = () => _seeds;
        ScrolllerDiskCache.UtcNow = () => _now;
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.UtcNow = () => DateTime.UtcNow;
        ScrolllerDiskCache.RootProvider = () => null;
        ScrolllerDiskCache.RootsFileProvider = () => null;
        ScrolllerDiskCache.SeedRootsProvider = () => Array.Empty<string>();
        try { Directory.Delete(_base, true); } catch { }
    }

    private string Put(string url)
    {
        using var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture"));
        Assert.NotNull(w);
        w!.Stream.Write(new byte[16], 0, 16);
        w.Commit();
        return ScrolllerDiskCache.TryGetPath(url)!;
    }

    /// <summary>A remote temp copy and a local temp file in the .temp beside a cache folder.</summary>
    private (string Remote, string Local) PutTemp(string root)
    {
        var temp = ScrolllerDiskCache.TempDirBeside(root)!;
        Directory.CreateDirectory(temp);
        var remote = Path.Combine(temp, RemoteMediaCache.TempPrefix + Guid.NewGuid().ToString("N") + ".jpg");
        var local = Path.Combine(temp, "ccp_temp_local_" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(remote, new byte[] { 1 });
        File.WriteAllBytes(local, new byte[] { 1 });
        return (remote, local);
    }

    /// <summary>A folder a shipped build filled: media under a name the cache would use, no list entry.</summary>
    private static string PlantMedia(string root)
    {
        Directory.CreateDirectory(root);
        var f = Path.Combine(root, ScrolllerDiskCache.FileNameFor("https://images.scrolller.com/old/" + Guid.NewGuid().ToString("N") + ".jpg"));
        File.WriteAllBytes(f, new byte[16]);
        return f;
    }

    private static string[] Media(string root)
        => Directory.Exists(root)
            ? Directory.GetFiles(root).Where(f => ScrolllerDiskCache.IsCacheFileName(Path.GetFileName(f))).ToArray()
            : Array.Empty<string>();

    [Fact]
    public void Switching_the_root_leaves_no_media_in_the_old_folder()
    {
        var path = Put("https://images.scrolller.com/roots/1.jpg");
        var (remote, local) = PutTemp(_rootA);
        Assert.True(File.Exists(path));

        _current = _rootB;
        ScrolllerDiskCache.SweepExpired();

        Assert.Empty(Media(_rootA));
        Assert.False(File.Exists(remote));
        Assert.True(File.Exists(local));   // only remote copies are ours to delete
    }

    [Fact]
    public void A_file_held_at_the_move_goes_on_the_next_sweep_then_the_folder_is_forgotten()
    {
        var path = Put("https://images.scrolller.com/roots/2.jpg");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _current = _rootB;
            ScrolllerDiskCache.SweepExpired();
            Assert.True(File.Exists(path));
        }
        Assert.Contains(ScrolllerDiskCache.KnownRoots(), r => r.Equals(Path.GetFullPath(_rootA), StringComparison.OrdinalIgnoreCase));
        ScrolllerDiskCache.SweepExpired();
        Assert.False(File.Exists(path));
        Assert.DoesNotContain(ScrolllerDiskCache.KnownRoots(), r => r.Equals(Path.GetFullPath(_rootA), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_folder_from_a_build_before_the_list_is_swept_through_the_seeds()
    {
        var oldRoot = Path.Combine(_base, "default-assets", ScrolllerDiskCache.FolderName);
        var planted = PlantMedia(oldRoot);
        var (remote, _) = PutTemp(oldRoot);
        _seeds = new[] { oldRoot, _rootA };
        Put("https://images.scrolller.com/roots/3.jpg");   // the live folder keeps its fresh copy

        ScrolllerDiskCache.SweepExpired();

        Assert.False(File.Exists(planted));
        Assert.False(File.Exists(remote));
        Assert.Single(Media(_rootA));
    }

    [Fact]
    public void The_off_switch_deletes_in_every_folder_ever_used()
    {
        Put("https://images.scrolller.com/roots/4.jpg");
        _current = _rootB;
        Put("https://images.scrolller.com/roots/5.jpg");
        var planted = PlantMedia(_rootA);   // the move's purge done, something lands there again

        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));

        Assert.False(File.Exists(planted));
        Assert.Empty(Media(_rootA));
        Assert.Empty(Media(_rootB));
    }

    [Fact]
    public void An_ordinary_sweep_with_no_current_folder_touches_nothing()
    {
        var planted = PlantMedia(_rootB);
        _seeds = new[] { _rootB };
        _current = null;
        Assert.Equal(0, ScrolllerDiskCache.SweepOldRoots(offSwitch: false));
        Assert.True(File.Exists(planted));
        Assert.True(ScrolllerDiskCache.SweepOldRoots(offSwitch: true) > 0);
        Assert.False(File.Exists(planted));
    }
}
