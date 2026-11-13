using System;
using System.IO;
using Avalonia;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Views.BackRoom;

// The room host owns this bridge and calls StopAll on teardown, on the UI thread.
internal sealed class BackRoomFlashBridge
{
    private readonly Action<int, double> _wash;
    private readonly Action<string, int, double, bool, Action> _hero;
    private readonly Action _stopHero;
    private readonly Action _close;
    private readonly Func<bool> _chaosRunning;
    private readonly Func<long> _now;
    private readonly string _assetsRoot, _webRoot;
    private long _washAt = long.MinValue / 2;

    internal BackRoomFlashBridge(Visual host, Func<bool> chaosRunning, string assetsRoot, string webRoot)
        : this((ms, alpha) => ChaosFlashOverlay.Show(host, ms, alpha),
            (path, ms, alpha, still, shown) => ChaosFlashOverlay.ShowHero(host, path, ms, alpha, still, shown),
            ChaosFlashOverlay.StopHero, ChaosFlashOverlay.CloseActive, chaosRunning,
            () => Environment.TickCount64, assetsRoot, webRoot) { }

    internal BackRoomFlashBridge(Action<int, double> wash, Action<string, int, double, bool, Action> hero,
        Action stopHero, Action close, Func<bool> chaosRunning, Func<long> now, string assetsRoot, string webRoot)
    {
        _wash = wash; _hero = hero; _stopHero = stopHero; _close = close;
        _chaosRunning = chaosRunning; _now = now; _assetsRoot = assetsRoot; _webRoot = webRoot;
    }

    internal void GlitchWash(int durationMs, double opacity)
    {
        _washAt = _now();
        _wash(durationMs, opacity);
    }

    internal void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown)
    {
        var path = LocalFile(gif.Url);
        if (path != null) _hero(path, durationMs, opacity, false, shown);
    }

    private string? LocalFile(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            var root = uri.Host switch { "ccp.assets" => _assetsRoot, "ccp.game" => _webRoot, _ => null };
            if (string.IsNullOrEmpty(root)) return null;
            var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0) return null;
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(prefix, relative));
            return path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { return null; }
    }

    internal void StopAll()
    {
        _stopHero();
        if (_now() - _washAt < 12000 && !_chaosRunning()) _close();
        _washAt = long.MinValue / 2;
    }
}
