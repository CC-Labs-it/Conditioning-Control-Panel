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
    private TimeSpan _delay;
    private DateTimeOffset _started;
    private bool _disposed;

    internal Task Decoding { get; } = Task.CompletedTask;

    /// <param name="fitLongSide">Pane long side in pixels (0 = decoder default), as CornerGif passes.</param>
    internal GazeRoundImage(string? path, int fitLongSide = 0)
    {
        Stretch = Stretch.Uniform;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        if (path == null) return;
        try { Source = new Bitmap(path); }
        catch (Exception ex) { Log.Warning(ex, "GazeMinigame: failed to load image {Path}", path); }
        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            Decoding = Decode(path, fitLongSide);
    }

    /// <summary>Render-proof constructor: an empty pane.</summary>
    internal GazeRoundImage() : this(null) { }

    private async Task Decode(string path, int fitLongSide)
    {
        // ponytail: same bounded decoder as MiniPlayer/CornerGif; uniform first-frame delay,
        // at most 120 frames and the pane's long side, rather than WPF's unbounded GIF playback.
        var (frames, delay) = await Task.Run(() => SpiralOverlay.Decode(path, fitLongSide));
        if (_disposed || frames.Count < 2)
        {
            foreach (var frame in frames) frame.Dispose();
            return;
        }
        (Source as Bitmap)?.Dispose();
        _frames = frames;
        _delay = delay;
        _started = GazeMinigameWindow.Clock.GetUtcNow();
        Source = frames[0];
    }

    /// <summary>Called on the game's 50ms clock, including its gaze grace period. No ambient loop.</summary>
    internal void AdvanceFrame(DateTimeOffset now)
    {
        if (_frames == null) return;
        var elapsed = Math.Max(0, (now - _started).TotalMilliseconds);
        var index = (int)((long)(elapsed / _delay.TotalMilliseconds) % _frames.Count);
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
