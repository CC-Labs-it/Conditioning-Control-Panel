using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>OS operations are serialized by WallpaperHost, never on the UI thread.</summary>
public interface IWallpaperPlatform
{
    string? Capture();
    bool Apply(string path);
    bool Restore(string snapshot);
}

/// <summary>Local wallpaper host. Keeps a durable original until restoration succeeds.</summary>
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

    public Task<bool> RestoreStaleAsync() => Queue(Restore);

    public Task<bool> ShuffleAsync() => Queue(() =>
    {
        if (_closed) return false;
        // A previous failed restore must succeed before we capture a fresh original.
        if (!IsActive && !string.IsNullOrEmpty(CoreSettings.Current.WallpaperOriginalPath) && !Restore()) return false;
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
        if (!_platform.Apply(image)) return false;
        _current = image;
        IsActive = true;
        return true;
    });

    public Task<bool> DeactivateAsync() => Queue(Restore);

    private bool Restore()
    {
        var original = _original ?? CoreSettings.Current.WallpaperOriginalPath;
        if (string.IsNullOrEmpty(original)) return true;
        if (!_platform.Restore(original)) return false; // Retain breadcrumb on failure for next launch.
        CoreSettings.Current.WallpaperOriginalPath = "";
        CoreSettings.SaveImmediate();
        _original = _current = null;
        IsActive = false;
        return true;
    }

    /// <summary>Exit drains all pending writes, then restores; subsequent shuffles are refused.</summary>
    public Task<bool> ShutdownAsync() => Queue(() => { _closed = true; return Restore(); });
}
