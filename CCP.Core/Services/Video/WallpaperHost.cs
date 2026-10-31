using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public Task<bool> ShuffleAsync() => Queue(() =>
    {
        if (_closed) return false;
        if (!IsActive && !string.IsNullOrEmpty(CoreSettings.Current.WallpaperOriginalPath)) Restore(once: true);
        var custom = CoreSettings.Current.WallpaperSourceFolder;
        var folder = Directory.Exists(custom) ? custom : Path.Combine(CorePaths.EffectiveAssets, "wallpapers");
        var pool = Directory.Exists(folder) ? Directory.GetFiles(folder)
            .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray() : [];
        if (pool.Length == 0) return false;
        if (!IsActive)
        {
            _original = _platform.Capture();
            if (string.IsNullOrWhiteSpace(_original)) return false;
            CoreSettings.Current.WallpaperOriginalPath = _original;
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
        return true;
    });
    public Task<bool> DeactivateAsync() => Queue(() => Restore());
    private bool Restore(bool once = false)
    {
        var original = _original ?? CoreSettings.Current.WallpaperOriginalPath;
        if (string.IsNullOrEmpty(original)) return true;
        var ok = _platform.Restore(original);
        if (!ok && !once) return false; // In session: keep the breadcrumb so the next launch tries once.
        if (!ok) Log.Warning("Stale wallpaper original {Path} could not be restored; clearing it", original);
        CoreSettings.Current.WallpaperOriginalPath = "";
        CoreSettings.SaveImmediate();
        _original = _current = null;
        IsActive = false;
        return ok;
    }
    public Task<bool> ShutdownAsync() => Queue(() => { _closed = true; return Restore(); });
}
