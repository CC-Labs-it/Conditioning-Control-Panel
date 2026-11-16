using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;
namespace ConditioningControlPanel.Services;
public interface IWallpaperPlatform
{
    string? Capture();
    bool Apply(string path);
    bool Restore(string snapshot);
}
public sealed class WallpaperHost
{
    private readonly IWallpaperPlatform _platform;
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private string? _original, _current;
    private bool _closed;
    // WPF WallpaperService.cs:52-96: a small pool of downloaded stills, filled in the background, and one deferred
    // retry when an activation found both pools empty. The retry only follows a fill that brought files, so a dead
    // CDN cannot spin it (WPF _remoteRetryConsumed).
    private const int RemotePoolTarget = 4;
    private readonly List<string> _remote = new();
    private int _fillInFlight;
    private bool _pending;
    /// <summary>Downloads up to n usable remote stills and returns their local files. Replaceable by tests.</summary>
    internal Func<int, CancellationToken, Task<List<string>>> RemoteFetch = FetchRemoteAsync;
    internal Action<string> ReleaseRemote = DtrhRemoteMedia.AssetsTempCopier.Release;
    internal Func<Models.AppSettings> Settings = () => CoreSettings.Current;
    /// <summary>The mixed-source roll, 0..n-1. Replaceable by tests.</summary>
    internal Func<int, int> Roll = n => Random.Shared.Next(n);
    internal Task LastFill { get; private set; } = Task.CompletedTask;
    public bool IsActive { get; private set; }
    public Task WhenIdle { get { lock (_gate) return _tail; } }
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    public WallpaperHost(IWallpaperPlatform platform) => _platform = platform;
    private Task<bool> Queue(Func<bool> action)
    {
        lock (_gate)
        {
            var next = _tail.ContinueWith(_ =>
            {
                try { return action(); }
                catch (Exception ex) { Log.Warning(ex, "Wallpaper operation failed"); return false; }
            }, TaskScheduler.Default);
            _tail = next;
            return next;
        }
    }
    public Task<bool> RestoreStaleAsync() => Queue(() => Restore(once: true));
    public Task<bool> ShuffleAsync() => Queue(Shuffle);
    private bool Shuffle()
    {
        if (_closed) return false;
        if (!IsActive && !string.IsNullOrEmpty(Settings().WallpaperOriginalPath)) Restore(once: true);
        var custom = Settings().WallpaperSourceFolder;
        var folder = Directory.Exists(custom) ? custom : Path.Combine(CorePaths.EffectiveAssets, "wallpapers");
        var pool = Directory.Exists(folder) ? Directory.GetFiles(folder)
            .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray() : [];
        KickRemoteFill(); // WPF :197, :352: never waits on a download
        string[] remote;
        lock (_remote) remote = RemoteEnabled() ? _remote.ToArray() : [];
        if (pool.Length == 0 && remote.Length == 0)
        {
            lock (_remote) _pending = !IsActive && RemoteEnabled(); // WPF :211
            return false;
        }
        // WPF PickImage :412: online draws remote, mixed rolls the ratio; an empty side falls back to the other.
        var s = Settings();
        var useRemote = remote.Length > 0 && (pool.Length == 0 || s.MediaSource == "online"
            || (s.MediaSource == "mixed" && Roll(100) < s.RemoteMediaRatio));
        if (useRemote) pool = remote;
        if (!IsActive)
        {
            _original = _platform.Capture();
            if (string.IsNullOrWhiteSpace(_original)) return false;
            Settings().WallpaperOriginalPath = _original;
            CoreSettings.SaveImmediate(); // Before the first write, including crash recovery (#692).
        }
        var choices = pool.Where(p => p != _current).ToArray();
        var image = choices.Length > 0 ? choices[Random.Shared.Next(choices.Length)] : pool[0];
        IsActive = true; // A failed write may still have changed part of the desktop.
        var applied = false;
        try { applied = _platform.Apply(image); }
        finally { if (!applied) Restore(); }
        if (!applied) return false;
        _current = image;
        lock (_remote) _pending = false;
        return true;
    }
    public Task<bool> DeactivateAsync()
    {
        lock (_remote) _pending = false; // at once: a pool landing after a stop must not put it back
        return Queue(() => Restore());
    }
    private bool Restore(bool once = false)
    {
        var original = _original ?? Settings().WallpaperOriginalPath;
        if (string.IsNullOrEmpty(original)) return true;
        var ok = _platform.Restore(original);
        if (!ok && ScrolllerCreditRules.IsScrolllerMedia(_current))
        {
            // WPF :258: no usable original while an online picture is up. It may not stay set (licence 3.4).
            var neutral = NeutralPath();
            if (neutral != null && _platform.Apply(neutral)) _current = neutral;
            Log.Warning("Wallpaper: no usable original to restore, a neutral picture replaces the online one");
        }
        if (!ok && !once) return false; // In session: keep the breadcrumb so the next launch tries once.
        if (!ok) Log.Warning("Stale wallpaper original {Path} could not be restored; clearing it", original);
        Settings().WallpaperOriginalPath = "";
        CoreSettings.SaveImmediate();
        _original = _current = null;
        IsActive = false;
        return ok;
    }
    public Task<bool> ShutdownAsync() => Queue(() =>
    {
        _closed = true;
        var ok = Restore();
        // WPF Dispose :929: the remote files go only after the desktop left them.
        string[] files;
        lock (_remote) { files = _remote.ToArray(); _remote.Clear(); _pending = false; }
        foreach (var file in files) Release(file);
        return ok;
    });
    private bool RemoteEnabled()
    {
        var s = Settings();
        return s.MediaSource != "local" && s.HasRemoteMediaConsent;
    }
    private void KickRemoteFill()
    {
        int wanted;
        lock (_remote) wanted = RemotePoolTarget - _remote.Count;
        if (_closed || wanted <= 0 || !RemoteEnabled() || Interlocked.CompareExchange(ref _fillInFlight, 1, 0) != 0) return;
        LastFill = Task.Run(async () =>
        {
            var retry = false;
            try
            {
                using var cts = new CancellationTokenSource(60000);
                var files = await RemoteFetch(wanted, cts.Token).ConfigureAwait(false);
                lock (_remote)
                {
                    if (_closed) { foreach (var file in files) Release(file); return; }
                    _remote.AddRange(files);
                    retry = files.Count > 0 && _pending;
                }
            }
            catch (Exception ex) { Log.Warning("Wallpaper: remote fill failed: {Error}", ex.Message); }
            finally { Interlocked.Exchange(ref _fillInFlight, 0); }
            // WPF RetryPendingActivation :546: one more go, rechecked in order behind any stop.
            if (retry) _ = Queue(() =>
            {
                lock (_remote)
                {
                    if (_closed || IsActive || !_pending) return false;
                    _pending = false;
                }
                return Shuffle();
            });
        });
    }
    private static async Task<List<string>> FetchRemoteAsync(int wanted, CancellationToken ct)
    {
        var coordinator = FypOnlineCoordinator.For("wallpaper", () => FypOnlineCoordinator.ResolveChannels(
            CoreSettings.Current.FypOnlineNiches, CoreSettings.Current.FypOnlineCustomSubs), FeedMediaKind.Image);
        var (entries, error) = await coordinator.FetchBatchAsync(ct).ConfigureAwait(false);
        var files = new List<string>();
        if (error != null) { Log.Warning("Wallpaper: remote fetch reported {Error}", error); return files; }
        foreach (var entry in entries)
        {
            if (files.Count >= wanted || ct.IsCancellationRequested) break;
            // WPF :471: webp and other formats the desktop will not take never cost a download.
            if (!RemoteMediaFormats.Validate(entry, FeedMediaKind.Image, out _) || !IsWallpaperFormat(entry.Url)) continue;
            var file = await DtrhRemoteMedia.AssetsTempCopier.Materialize(entry.Url, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(file)) continue;
            if (IsWallpaperFormat(file) && File.Exists(file)) files.Add(file);
            else DtrhRemoteMedia.AssetsTempCopier.Release(file);
        }
        return files;
    }
    internal static bool IsWallpaperFormat(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var cut = path.IndexOfAny(['?', '#']);
        return Extensions.Contains(Path.GetExtension(cut >= 0 ? path[..cut] : path).ToLowerInvariant());
    }
    /// <summary>WPF EnforceRemoteMaxAge :716: no remote wallpaper stays in the pool, on disk or on the desktop past a
    /// day from its download; <paramref name="all"/> (the off switch) takes every one. True when any went.</summary>
    public Task<bool> EnforceRemoteMaxAgeAsync(bool all = false) => Queue(() =>
    {
        List<string> stale;
        var at = DateTime.UtcNow + ScrolllerDiskCache.SweepPeriod; // gone before the next hourly sweep would be late
        lock (_remote)
        {
            stale = _remote.Where(p => all || ScrolllerDiskCache.CopyDue(p, t => ScrolllerCacheBook.IsExpiredAt(t, at))).ToList();
            _remote.RemoveAll(stale.Contains);
        }
        if (stale.Count == 0) return false;
        if (IsActive && _current != null && stale.Contains(_current) && !Shuffle()) Restore();
        foreach (var file in stale) Release(file);
        Log.Information("Wallpaper: {Count} online wallpaper(s) past the day limit removed", stale.Count);
        return true;
    });
    private void Release(string file)
    {
        try { ReleaseRemote(file); }
        catch (Exception ex) { Log.Debug("Wallpaper: temp release failed: {Error}", ex.Message); }
    }
    /// <summary>WPF NeutralWallpaperPath :674: a plain dark picture written once beside the user data.</summary>
    private static string? NeutralPath()
    {
        try
        {
            var path = Path.Combine(CorePaths.UserData, "wallpaper-neutral.bmp");
            if (!File.Exists(path)) { Directory.CreateDirectory(CorePaths.UserData); File.WriteAllBytes(path, SolidBmp(16, 16, 0x22, 0x20, 0x24)); }
            return path;
        }
        catch { return null; }
    }
    internal static byte[] SolidBmp(int width, int height, byte r, byte g, byte b)
    {
        int row = (width * 3 + 3) & ~3, size = 54 + row * height;
        var bmp = new byte[size];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(row * height).CopyTo(bmp, 34);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) { int i = 54 + y * row + x * 3; bmp[i] = b; bmp[i + 1] = g; bmp[i + 2] = r; }
        return bmp;
    }
}
