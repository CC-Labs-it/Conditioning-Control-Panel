using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.ScreenCapture;

/// <summary>Captures desktop pixels. Unavailable includes denied permission; callers must not retry a consent prompt in a scan loop.</summary>
public interface IScreenFrameSource
{
    Task<ScreenFrameResult> CaptureAsync(CancellationToken cancellationToken);
}

public enum ScreenFrameStatus { Available, Unavailable }
public enum ScreenPixelFormat { Bgra32, Rgba32 }

/// <param name="ScreenBounds">Monitor bounds in physical desktop pixels, including negative origins.</param>
/// <param name="Scale">Monitor UI scale; pixel coordinates are already physical and must not be scaled twice.</param>
/// <param name="Pixels">Top-down rows, with <paramref name="Stride"/> bytes per row. The source must not mutate the buffer after returning it.</param>
public sealed record ScreenFrame(string Screen, Rectangle ScreenBounds, double Scale, int Width, int Height,
    int Stride, ScreenPixelFormat Format, ReadOnlyMemory<byte> Pixels)
{
    /// <summary>Map a rectangle in frame pixels to physical desktop pixels, including resized captures.</summary>
    public Rectangle ToScreen(Rectangle bounds)
    {
        if (Width <= 0 || Height <= 0 || ScreenBounds.Width <= 0 || ScreenBounds.Height <= 0)
            throw new InvalidOperationException("Frame and screen dimensions must be positive.");
        int left = ScreenBounds.X + (int)Math.Floor((double)bounds.Left * ScreenBounds.Width / Width);
        int top = ScreenBounds.Y + (int)Math.Floor((double)bounds.Top * ScreenBounds.Height / Height);
        int right = ScreenBounds.X + (int)Math.Ceiling((double)bounds.Right * ScreenBounds.Width / Width);
        int bottom = ScreenBounds.Y + (int)Math.Ceiling((double)bounds.Bottom * ScreenBounds.Height / Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

public sealed record ScreenFrameResult(ScreenFrameStatus Status, IReadOnlyList<ScreenFrame> Frames)
{
    public static ScreenFrameResult Unavailable { get; } = new(ScreenFrameStatus.Unavailable, Array.Empty<ScreenFrame>());
}

/// <summary>Per-monitor change detector. Reset when the scanner stops or monitor topology changes.</summary>
public sealed class ScreenFrameChanges
{
    private readonly Dictionary<string, (Rectangle Bounds, int Width, int Height, int Stride, ScreenPixelFormat Format, ulong Hash)> _last = new();

    public bool Changed(ScreenFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride < (long)frame.Width * 4 ||
            frame.Pixels.Length < (long)frame.Stride * frame.Height)
            throw new ArgumentException("Invalid frame dimensions or pixel buffer.", nameof(frame));
        // FNV-1a over visible pixels only: padding may be uninitialised and cause spurious changes.
        ulong hash = 14695981039346656037UL;
        var pixels = frame.Pixels.Span;
        for (int y = 0; y < frame.Height; y++)
            foreach (byte pixel in pixels.Slice(y * frame.Stride, frame.Width * 4))
                hash = unchecked((hash ^ pixel) * 1099511628211UL);
        var current = (frame.ScreenBounds, frame.Width, frame.Height, frame.Stride, frame.Format, hash);
        bool changed = !_last.TryGetValue(frame.Screen, out var previous) || previous != current;
        _last[frame.Screen] = current;
        return changed;
    }

    public void Reset() => _last.Clear();
}
