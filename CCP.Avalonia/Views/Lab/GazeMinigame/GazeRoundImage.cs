using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Serilog;
using SkiaSharp;

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
    private string? _streamPath;
    private int _streamWidth, _streamHeight;
    private SKCodec? _streamCodec;
    private SKBitmap? _streamCanvas;
    private int[]? _requiredFrames;
    private int _priorFrame = -1;
    private long _wantedFrame, _displayedFrame;
    private bool _reading;
    private readonly CancellationTokenSource _readCancellation = new();
    internal Task FrameDecoding { get; private set; } = Task.CompletedTask;
    internal int CachedFrameCount => _frames?.Count ?? 0;

    internal Task Decoding { get; } = Task.CompletedTask;

    /// <param name="fitLongSide">Pane long side in pixels (0 = decoder default), as CornerGif passes.</param>
    internal GazeRoundImage(string? path, int fitLongSide = 0, long cacheBudget = SpiralFrames.MaxCacheBytes)
    {
        Stretch = Stretch.Uniform;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        if (path == null) return;
        try { Source = new Bitmap(path); }
        catch (Exception ex) { Log.Warning(ex, "GazeMinigame: failed to load image {Path}", path); }
        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            Decoding = Decode(path, fitLongSide, cacheBudget);
    }

    /// <summary>Render-proof constructor: an empty pane.</summary>
    internal GazeRoundImage() : this(null) { }

    private async Task Decode(string path, int fitLongSide, long cacheBudget)
    {
        // WPF GazeMinigameWindow.xaml.cs:935-939: animate GIFs at their timing forever.
        // Large GIFs decode the requested frame on a worker instead of sampling the timeline.
        try
        {
            var probe = await Task.Run(() => Probe(path, fitLongSide, cacheBudget));
            if (_disposed) return;
            if (probe.Durations != null)
            {
                _streamPath = path;
                (_streamWidth, _streamHeight) = (probe.Width, probe.Height);
                _durations.AddRange(probe.Durations);
            }
            else
            {
                var (frames, _) = await Task.Run(() => SpiralOverlay.Decode(path, fitLongSide, frameDurations: _durations,
                    allFramesBudgetBytes: cacheBudget));
                if (_disposed || frames.Count < 2)
                {
                    foreach (var frame in frames) frame.Dispose();
                    return;
                }
                (Source as Bitmap)?.Dispose();
                _frames = frames;
                Source = frames[0];
            }
            foreach (var duration in _durations) _loopMs += duration.TotalMilliseconds;
            _started = GazeMinigameWindow.Clock.GetUtcNow();
            _timer = new DispatcherTimer();
            _timer.Tick += OnFrameTick;
            ScheduleFrame();
        }
        catch (Exception ex) { Log.Warning(ex, "GazeMinigame: failed to decode GIF {Path}", path); }
    }

    private static (TimeSpan[]? Durations, int Width, int Height) Probe(string path, int fitLongSide, long budget)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null || codec.FrameCount < 2) return (null, 0, 0);
        var plan = SpiralFrames.Plan(codec.Info.Width, codec.Info.Height, codec.FrameCount, codec.FrameInfo[0].Duration);
        if (fitLongSide > 0 && Math.Max(plan.Width, plan.Height) > fitLongSide)
        {
            var scale = fitLongSide / (double)Math.Max(plan.Width, plan.Height);
            plan.Width = Math.Max(1, (int)Math.Round(plan.Width * scale));
            plan.Height = Math.Max(1, (int)Math.Round(plan.Height * scale));
        }
        if ((long)codec.FrameCount * plan.Width * plan.Height * 4 <= budget) return (null, 0, 0);
        var info = codec.FrameInfo;
        var durations = new TimeSpan[codec.FrameCount];
        for (int i = 0; i < durations.Length; i++)
            durations[i] = TimeSpan.FromMilliseconds(i < info.Length && info[i].Duration > 0 ? info[i].Duration : 100);
        return (durations, plan.Width, plan.Height);
    }

    private async Task ReadRequestedFrames()
    {
        _reading = true;
        try
        {
            while (!_disposed && _displayedFrame < _wantedFrame)
            {
                int index = (int)((_displayedFrame + 1) % _durations.Count);
                string path = _streamPath!;
                int width = _streamWidth, height = _streamHeight;
                var token = _readCancellation.Token;
                var frame = await Task.Run(() => SpiralOverlay.RunGatedDecode(() => ReadFrame(path, index, width, height), token), token);
                if (frame == null) return;
                if (_disposed) { frame.Dispose(); return; }
                var old = Source as Bitmap;
                Source = frame;
                _displayedFrame++;
                old?.Dispose();
            }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex) { Log.Warning(ex, "GazeMinigame: failed to decode GIF frame"); }
        finally
        {
            _reading = false;
            if (_disposed) ReleaseDecoder();
        }
    }

    private Bitmap? ReadFrame(string path, int index, int width, int height)
    {
        if (_streamCodec == null)
        {
            _streamCodec = SKCodec.Create(path);
            if (_streamCodec == null) return null;
            var info = _streamCodec.FrameInfo;
            _requiredFrames = new int[info.Length];
            for (int i = 0; i < info.Length; i++) _requiredFrames[i] = info[i].RequiredFrame;
            _streamCanvas = new SKBitmap(new SKImageInfo(_streamCodec.Info.Width, _streamCodec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul));
        }
        var full = _streamCanvas!.Info;
        int prior = index > 0 && _priorFrame == index - 1 && index < _requiredFrames!.Length
            && _requiredFrames[index] == _priorFrame ? _priorFrame : -1;
        var result = _streamCodec.GetPixels(full, _streamCanvas.GetPixels(), new SKCodecOptions(index, prior));
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) return null;
        _priorFrame = index;
        using var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        _streamCanvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
            new PixelSize(width, height), new Vector(96, 96), scaled.RowBytes);
    }

    private void ReleaseDecoder()
    {
        _streamCanvas?.Dispose();
        _streamCodec?.Dispose();
        _readCancellation.Dispose();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        AdvanceFrame(GazeMinigameWindow.Clock.GetUtcNow());
        ScheduleFrame();
    }

    private void ScheduleFrame()
    {
        if (_timer == null || _durations.Count < 2) return;
        var phase = Math.Max(0, (GazeMinigameWindow.Clock.GetUtcNow() - _started).TotalMilliseconds) % _loopMs;
        var index = 0;
        while (index < _durations.Count - 1 && phase >= _durations[index].TotalMilliseconds)
            phase -= _durations[index++].TotalMilliseconds;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _durations[index].TotalMilliseconds - phase));
        _timer.Start();
    }

    /// <summary>Advance to the frame at the current loop phase.</summary>
    internal void AdvanceFrame(DateTimeOffset now)
    {
        if (_disposed || (_frames == null && _streamPath == null)) return;
        var elapsed = Math.Max(0, (now - _started).TotalMilliseconds);
        var phase = elapsed % _loopMs;
        var index = 0;
        while (index < _durations.Count - 1 && phase >= _durations[index].TotalMilliseconds)
            phase -= _durations[index++].TotalMilliseconds;
        if (_frames != null) Source = _frames[index];
        else
        {
            _wantedFrame = (long)(elapsed / _loopMs) * _durations.Count + index;
            if (!_reading && _displayedFrame < _wantedFrame) FrameDecoding = ReadRequestedFrames();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readCancellation.Cancel();
        if (!_reading) ReleaseDecoder();
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
