using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
/// <summary>WPF WallpaperService.cs:186-563 (remote pool, pick, deferred retry) and :646-760 (neutral cover, day limit).</summary>
public sealed class WallpaperRemotePoolTests : IDisposable
{
    private sealed class Desktop : IWallpaperPlatform
    {
        public readonly List<string> Applied = new();
        public bool RestoreWorks = true;
        public int Restores;
        public string? Capture() => "original";
        public bool Apply(string path) { Applied.Add(path); return true; }
        public bool Restore(string snapshot) { Restores++; return RestoreWorks; }
    }
    private readonly string _dir = Directory.CreateTempSubdirectory("wallpaper-remote-").FullName;
    private readonly string _local;
    /// <summary>Its own settings: other suites swap the shared provider in parallel.</summary>
    private readonly AppSettings _s;
    public WallpaperRemotePoolTests()
    {
        _local = Directory.CreateDirectory(Path.Combine(_dir, "local")).FullName;
        _s = new AppSettings { WallpaperSourceFolder = _local, WallpaperOriginalPath = "", MediaSource = "online", RemoteMediaConsented = true };
    }
    public void Dispose() => Directory.Delete(_dir, true);
    private string Remote(string name)
    {
        var path = Path.Combine(_dir, "ccp_temp_remote_" + name);
        File.WriteAllText(path, "fixture");
        return path;
    }
    /// <summary>Each download is a new file, so a fake hands its files out once.</summary>
    private static Func<int, CancellationToken, Task<List<string>>> Once(params string[] files)
    {
        var left = files.ToList();
        return (_, _) => { var batch = left; left = new(); return Task.FromResult(batch); };
    }
    private WallpaperHost Host(Desktop desktop, Func<int, CancellationToken, Task<List<string>>> fetch, List<string> released)
        => new(desktop) { RemoteFetch = fetch, ReleaseRemote = released.Add, Settings = () => _s };

    [Theory]
    [InlineData("https://cdn.example/a.jpg?w=1", true)]
    [InlineData("https://cdn.example/a.png#x", true)]
    [InlineData("https://cdn.example/a.webp", false)]
    [InlineData("https://cdn.example/a.mp4", false)]
    public void OnlyRealWallpaperFormatsAreKept(string url, bool kept) => Assert.Equal(kept, WallpaperHost.IsWallpaperFormat(url));

    [Fact]
    public async Task EmptyLibraryRetriesOnceWhenTheRemotePoolLands()
    {
        var gate = new TaskCompletionSource<List<string>>();
        var asked = new List<int>();
        var desktop = new Desktop();
        var host = Host(desktop, (n, _) => { asked.Add(n); return gate.Task; }, new());
        Assert.False(await host.ShuffleAsync());
        Assert.Equal(new[] { 4 }, asked);
        gate.SetResult(new() { Remote("a.jpg") });
        await host.LastFill;
        await host.WhenIdle;
        Assert.True(host.IsActive);
        Assert.Equal(Path.Combine(_dir, "ccp_temp_remote_a.jpg"), desktop.Applied.Single());
    }

    [Fact]
    public async Task AnEmptyFillNeverRetriesAndAStopCancelsTheRetry()
    {
        var desktop = new Desktop();
        var host = Host(desktop, (_, _) => Task.FromResult(new List<string>()), new());
        Assert.False(await host.ShuffleAsync());
        await host.LastFill;
        await host.WhenIdle;
        Assert.Empty(desktop.Applied);
        var gate = new TaskCompletionSource<List<string>>();
        host.RemoteFetch = (_, _) => gate.Task;
        Assert.False(await host.ShuffleAsync());
        await host.DeactivateAsync(); // panic and every stop path
        gate.SetResult(new() { Remote("late.jpg") });
        await host.LastFill;
        await host.WhenIdle;
        Assert.False(host.IsActive);
        Assert.Empty(desktop.Applied);
    }

    [Fact]
    public async Task SourceSettingPicksThePoolAndAnEmptySideFallsBack()
    {
        var local = Path.Combine(_local, "own.png");
        File.WriteAllText(local, "fixture");
        var remote = Remote("r.png");
        var desktop = new Desktop();
        var host = Host(desktop, Once(remote), new());
        _s.MediaSource = "mixed";
        _s.RemoteMediaRatio = 30; // the setter clamps to 5..95, so 0 would still draw remote 5 % of the time
        host.Roll = _ => 30; // a roll at the ratio stays local
        Assert.True(await host.ShuffleAsync()); // pool still cold: local
        await host.LastFill;
        Assert.True(await host.ShuffleAsync());
        Assert.Equal(local, desktop.Applied[^1]);
        host.Roll = _ => 29; // a roll under the ratio draws remote
        Assert.True(await host.ShuffleAsync());
        Assert.Equal(remote, desktop.Applied[^1]);
        _s.MediaSource = "online";
        Assert.True(await host.ShuffleAsync());
        Assert.Equal(remote, desktop.Applied[^1]);
        _s.MediaSource = "local";
        Assert.True(await host.ShuffleAsync());
        Assert.Equal(local, desktop.Applied[^1]);
        _s.MediaSource = "online";
        File.Delete(local);
        _s.RemoteMediaConsented = false;
        Assert.False(await host.ShuffleAsync()); // no consent: the remote pool is never drawn
        Assert.Equal(5, desktop.Applied.Count);
    }

    [Fact]
    public async Task DayLimitMovesTheDesktopOffAndTheOffSwitchTakesEveryFile()
    {
        var stale = Remote("old.jpg");
        var fresh = Remote("new.jpg");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-23.5));
        var released = new List<string>();
        var desktop = new Desktop();
        var host = Host(desktop, Once(stale), released);
        Assert.False(await host.ShuffleAsync());
        await host.LastFill;
        await host.WhenIdle;
        Assert.Equal(stale, desktop.Applied.Single());
        host.RemoteFetch = Once(fresh);
        Assert.True(await host.ShuffleAsync()); // only the stale one to show, but the fill runs
        await host.LastFill;
        Assert.True(await host.EnforceRemoteMaxAgeAsync());
        Assert.Equal(new[] { stale }, released);
        Assert.Equal(fresh, desktop.Applied[^1]);
        Assert.True(host.IsActive);
        host.RemoteFetch = (_, _) => Task.FromResult(new List<string>());
        Assert.True(await host.EnforceRemoteMaxAgeAsync(all: true));
        Assert.Equal(new[] { stale, fresh }, released);
        Assert.False(host.IsActive); // nothing else to show: the user's own came back
        Assert.Equal(1, desktop.Restores);
    }

    [Fact]
    public async Task NoUsableOriginalCoversAnOnlinePictureAndShutdownReleasesThePool()
    {
        var remote = Remote("on.jpg");
        var released = new List<string>();
        var desktop = new Desktop { RestoreWorks = false };
        var host = Host(desktop, Once(remote), released);
        Assert.False(await host.ShuffleAsync());
        await host.LastFill;
        await host.WhenIdle;
        Assert.Equal(remote, desktop.Applied.Single());
        Assert.False(await host.DeactivateAsync());
        Assert.Equal("wallpaper-neutral.bmp", Path.GetFileName(desktop.Applied[^1]));
        Assert.True(File.Exists(desktop.Applied[^1]));
        Assert.NotEqual("", _s.WallpaperOriginalPath); // the next launch still gets its go
        await host.ShutdownAsync();
        Assert.Equal(new[] { remote }, released);
    }
}
