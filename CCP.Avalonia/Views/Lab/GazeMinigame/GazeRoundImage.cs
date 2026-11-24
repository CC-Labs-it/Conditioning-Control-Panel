using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    private DispatcherTimer? _timer;

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
        // WPF GazeMinigameWindow.xaml.cs:935-939: animate GIFs with source timing and repeat forever.
        var (frames, _) = await Task.Run(() => SpiralOverlay.Decode(path, fitLongSide, frameDurations: _durations, allFrames: true));
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
        _timer = new DispatcherTimer();
        _timer.Tick += OnFrameTick;
        ScheduleFrame();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        AdvanceFrame(GazeMinigameWindow.Clock.GetUtcNow());
        ScheduleFrame();
    }

    private void ScheduleFrame()
    {
        if (_timer == null || _frames == null) return;
        var phase = Math.Max(0, (GazeMinigameWindow.Clock.GetUtcNow() - _started).TotalMilliseconds) % _loopMs;
        var index = 0;
        while (index < _frames.Count - 1 && phase >= _durations[index].TotalMilliseconds)
            phase -= _durations[index++].TotalMilliseconds;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _durations[index].TotalMilliseconds - phase));
        _timer.Start();
    }

    /// <summary>Advance to the frame at the current loop phase.</summary>
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
        _timer?.Stop();
        _timer = null;
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
