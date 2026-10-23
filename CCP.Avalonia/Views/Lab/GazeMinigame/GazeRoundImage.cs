using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;

/// <summary>A round owns its bitmaps, including a decode that finishes after round disposal.</summary>
internal sealed class GazeRoundImage : Image, IDisposable
{
    private List<Bitmap>? _frames;
    private readonly List<TimeSpan> _durations = new();
    private double _loopMs;
    private DateTimeOffset _started;
    private bool _disposed;

    internal Task Decoding { get; } = Task.CompletedTask;

    internal GazeRoundImage(string path)
    {
        Stretch = Stretch.Uniform;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        try { Source = new Bitmap(path); }
        catch (Exception ex) { Log.Warning(ex, "GazeMinigame: failed to load image {Path}", path); }
        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            Decoding = Decode(path);
    }

    private async Task Decode(string path)
    {
        // ponytail: retain the shared decoder's memory/size budget instead of WPF's unbounded
        // cache. Preserve source timing, summing skipped frames into the sampled frame's hold.
        var (frames, _) = await Task.Run(() => SpiralOverlay.Decode(path, frameDurations: _durations));
        if (_disposed || frames.Count < 2)
        {
            foreach (var frame in frames) frame.Dispose();
            return;
        }
        (Source as Bitmap)?.Dispose();
        _frames = frames;
        foreach (var duration in _durations) _loopMs += duration.TotalMilliseconds;
        _started = GazeMinigameWindow.Clock.GetUtcNow();
        Source = frames[0];
    }

    /// <summary>Called on the game's 50ms clock, including its gaze grace period. No ambient loop.</summary>
    internal void AdvanceFrame(DateTimeOffset now)
    {
        if (_frames == null) return;
        var elapsed = Math.Max(0, (now - _started).TotalMilliseconds);
        var phase = elapsed % _loopMs;
        var index = 0;
        while (index < _frames.Count - 1 && phase >= _durations[index].TotalMilliseconds)
            phase -= _durations[index++].TotalMilliseconds;
        Source = _frames[index];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var still = Source as Bitmap;
        Source = null;
        if (_frames != null)
        {
            foreach (var frame in _frames) frame.Dispose();
            _frames = null;
        }
        else still?.Dispose();
    }
}
