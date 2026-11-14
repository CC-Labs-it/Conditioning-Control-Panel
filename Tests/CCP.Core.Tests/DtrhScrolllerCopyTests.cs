using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// DtRH's online copies on the Avalonia head (Core DtrhRemoteMedia.AssetsTempCopier) follow the Scrolller licence as
/// WPF's RemoteMediaCache does: no spoofed browser user-agent or Referer, the device cache (cached = free, fresh =
/// charged, refused when the share is spent), nothing while the switch is off, and a copy born at its download time.
/// </summary>
[Collection(SessionStatics.Name)]   // ScrolllerPolicy and the copier seam are process-wide
public sealed class DtrhScrolllerCopyTests : IDisposable
{
    private const string Url = "https://images.scrolller.com/some/clip-12345.jpg";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-dtrh-copy-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string, long, CancellationToken, Task<string?>> _realFetch = DtrhRemoteMedia.DeviceCacheFetch;

    public void Dispose()
    {
        ScrolllerPolicy.Apply(true, null);
        DtrhRemoteMedia.DeviceCacheFetch = _realFetch;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void The_copier_never_spoofs_a_browser_or_the_cdns_own_site()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Chaos", "DtrhRemoteMedia.cs"));
        Assert.DoesNotContain("Mozilla/5.0", src);
        Assert.DoesNotContain("\"Referer\"", src);
        Assert.Contains("ScrolllerSource.HonestUserAgent", src);
    }

    [Fact]
    public async Task Switch_off_refuses_before_the_cache_is_asked()
    {
        int asked = 0;
        DtrhRemoteMedia.DeviceCacheFetch = (_, _, _) => { asked++; return Task.FromResult<string?>(null); };
        ScrolllerPolicy.Apply(false, null);
        Assert.Null(await DtrhRemoteMedia.CopyScrolllerAsync(Url, _dir, CancellationToken.None));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_refused_fetch_leaves_no_copy()
    {
        // The device cache answers null when the share is spent (MayFetchFresh) or consent is missing.
        DtrhRemoteMedia.DeviceCacheFetch = (_, _, _) => Task.FromResult<string?>(null);
        Assert.Null(await DtrhRemoteMedia.CopyScrolllerAsync(Url, _dir, CancellationToken.None));
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    [Fact]
    public async Task A_cached_copy_is_copied_under_the_copy_prefix_and_capped()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, "cached.jpg");
        await File.WriteAllBytesAsync(cached, new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3 });
        long asked = 0;
        DtrhRemoteMedia.DeviceCacheFetch = (url, max, _) => { asked = max; return Task.FromResult<string?>(url == Url ? cached : null); };
        var copy = await DtrhRemoteMedia.CopyScrolllerAsync(Url, Path.Combine(_dir, ".temp"), CancellationToken.None);
        Assert.NotNull(copy);
        Assert.True(DtrhRemoteMedia.IsCopyFile(copy!));
        Assert.EndsWith(".jpg", copy);
        Assert.Equal(DtrhRemoteMedia.MaxCopyBytes, asked);
        Assert.Equal(await File.ReadAllBytesAsync(cached), await File.ReadAllBytesAsync(copy!));
    }

    [Fact]
    public async Task An_oversized_cached_file_is_not_copied()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, "big.jpg");
        using (var f = File.Create(cached)) f.SetLength(DtrhRemoteMedia.MaxCopyBytes + 1);
        DtrhRemoteMedia.DeviceCacheFetch = (_, _, _) => Task.FromResult<string?>(cached);
        Assert.Null(await DtrhRemoteMedia.CopyScrolllerAsync(Url, Path.Combine(_dir, ".temp"), CancellationToken.None));
    }

    // WPF RemoteMediaCache.cs:475-482 uses a disk hit before the fresh-download consent gate.
    [Fact]
    public async Task Game_only_consent_can_use_a_cached_hit_but_not_fetch_a_miss()
    {
        var (root, now, consent, warm, format, cap, age) = (ScrolllerDiskCache.RootProvider,
            ScrolllerDiskCache.UtcNow, ScrolllerDiskCache.RemoteConsent, ScrolllerDiskCache.WarmInline,
            ScrolllerDiskCache.FormatCheck, ScrolllerDiskCache.CapBytes, ScrolllerDiskCache.MaxAge);
        var (enabled, clipCap) = (ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        try
        {
            ScrolllerPolicy.Apply(true, clipCap);
            ScrolllerDiskCache.ResetForTests();
            ScrolllerDiskCache.RootProvider = () => Path.Combine(_dir, "cache");
            ScrolllerDiskCache.RemoteConsent = () => false;
            using (var writer = ScrolllerDiskCache.BeginWrite(Url, null))
            {
                Assert.NotNull(writer);
                await writer!.Stream.WriteAsync(new byte[] { 0xFF, 0xD8, 0xFF, 1 });
                writer.Commit();
            }
            // Reopen with a cold asynchronous index, as on a quick launch after restart.
            ScrolllerDiskCache.Flush();
            ScrolllerDiskCache.ResetForTests();
            ScrolllerDiskCache.RootProvider = () => Path.Combine(_dir, "cache");
            ScrolllerDiskCache.WarmInline = false;
            ScrolllerDiskCache.RemoteConsent = () => false;
            int fetches = 0;
            DtrhRemoteMedia.DeviceCacheFetch = (url, max, ct) =>
            {
                fetches++;
                return ScrolllerDiskCache.GetOrFetchAsync(url, null, max, ct);
            };
            var copy = await DtrhRemoteMedia.CopyScrolllerAsync(Url, Path.Combine(_dir, "copies"), CancellationToken.None);
            Assert.NotNull(copy);
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 1 }, await File.ReadAllBytesAsync(copy!));
            Assert.Equal(0, fetches);
            Assert.Null(await DtrhRemoteMedia.CopyScrolllerAsync(Url + "-missing", Path.Combine(_dir, "copies"), CancellationToken.None));
            Assert.Equal(1, fetches);
        }
        finally
        {
            ScrolllerDiskCache.ResetForTests();
            (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.UtcNow, ScrolllerDiskCache.RemoteConsent,
                ScrolllerDiskCache.WarmInline, ScrolllerDiskCache.FormatCheck, ScrolllerDiskCache.CapBytes,
                ScrolllerDiskCache.MaxAge) = (root, now, consent, warm, format, cap, age);
            ScrolllerPolicy.Apply(enabled, clipCap);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Core", "Services"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
