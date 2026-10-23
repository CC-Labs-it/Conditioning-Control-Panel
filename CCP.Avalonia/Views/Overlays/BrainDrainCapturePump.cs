using System;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// Ownership/blur curves adapted from WPF BrainDrainCapturePump/BrainDrainLayer and trunk
// 11c20f4ef3d96b06386a69e655d4307406a5f7fe. No native calls, joins or waits on the UI thread.
internal sealed class BrainDrainCapturePump : IDisposable
{
    internal sealed record Input(PixelRect Bounds, int Strength, bool Melt, int Downscale, int Fps, IntPtr[] Excluded);
    internal sealed record Frame(SKImage Image, Input Input, double At) : IDisposable
    { public void Dispose() => Image.Dispose(); }
    private static readonly object Closed = new();
    private object? _pending;
    private Input _input;
    private readonly Func<IDesktopCapture>? _factory;
    private readonly Func<double> _now;
    private double _lastFrame;
    private volatile string? _failure;
    internal SKImage? PendingImageForTest => (Volatile.Read(ref _pending) as Frame)?.Image;
    internal bool Stopped => ReferenceEquals(Volatile.Read(ref _pending), Closed);
    // Like WPF, one bad frame keeps the last blur; a failure shows only once no frame is fresh (3 s).
    internal string? Notice => _factory == null ? _failure : _now() - Volatile.Read(ref _lastFrame) < 3 ? null
        : _failure ?? "Brain Drain: no fresh desktop frame. Showing a flat veil, not blur.";
    internal BrainDrainCapturePump(Input input, Func<IDesktopCapture>? factory, Func<double>? now = null, bool start = true)
    {
        _input = input; _factory = factory; _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _lastFrame = _now();
        if (factory == null) _failure = "Brain Drain: desktop capture unavailable on this backend (including native Wayland). Showing a flat veil, not blur.";
        else if (start) new Thread(Run) { IsBackground = true, Name = "BrainDrain-Capture" }.Start();
    }
    internal void Update(Input input) => Volatile.Write(ref _input, input);
    internal Frame? Take()
    {
        while (true)
        {
            var current = Volatile.Read(ref _pending);
            if (current is not Frame frame) return null;
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _pending, null, current), current)) continue;
            if (ReferenceEquals(frame.Input, Volatile.Read(ref _input)) && _now() - frame.At < 3) return frame;
            frame.Dispose();
        }
    }
    public void Dispose() => (Interlocked.Exchange(ref _pending, Closed) as Frame)?.Dispose();
    private void Run()
    {
        try
        {
            using var capture = _factory!();
            while (!Stopped)
            {
                var at = _now();
                CaptureOnce(capture);
                if (!Stopped) Thread.Sleep(Math.Max(1, (int)(1000.0 / Math.Clamp(_input.Fps, 1, 60) - (_now() - at) * 1000)));
            }
        }
        catch (Exception ex) { _failure = "Brain Drain: " + ex.Message + ". Showing a flat veil, not blur."; }
    }
    internal void CaptureOnce(IDesktopCapture capture)
    {
        var input = Volatile.Read(ref _input);
        try
        {
            using var raw = capture.Capture(input.Bounds, input.Downscale, input.Excluded);
            using (var pixels = raw.PeekPixels())
            {
                bool blank = true;
                for (int y = 0; y < pixels.Height && blank; y++)
                    for (int x = 0; x < pixels.Width && blank; x++)
                    { var c = pixels.GetPixelColor(x, y); blank = c.Alpha == 0 || (c.Red == 0 && c.Green == 0 && c.Blue == 0); }
                if (blank) throw new InvalidOperationException("Capture returned only black/empty pixels");
            }
            var image = Blur(raw, input, (float)_now());
            var frame = new Frame(image, input, _now());
            while (true)
            {
                var old = Volatile.Read(ref _pending);
                if (ReferenceEquals(old, Closed) || !ReferenceEquals(input, Volatile.Read(ref _input))) { frame.Dispose(); return; }
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _pending, frame, old), old)) continue;
                (old as Frame)?.Dispose();
                Volatile.Write(ref _lastFrame, frame.At); _failure = null;
                return;
            }
        }
        catch (Exception ex)
        {
            _failure = "Brain Drain: " + ex.Message + ". Showing a flat veil, not blur.";
            Take()?.Dispose(); // failure never leaves a queued success to display
        }
    }
    internal static SKImage Blur(SKImage raw, Input input, float seconds)
    {
        using var surface = SKSurface.Create(new SKImageInfo(raw.Width, raw.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var strength = Math.Clamp(input.Strength, 0, 200);
        float sigma = (float)(strength * .14 / Math.Max(1, input.Downscale) / 3);
        using var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var noise = input.Melt ? SKShader.CreatePerlinNoiseTurbulence(.02f, .013f, 2, 7) : null;
        using var drift = noise?.WithLocalMatrix(SKMatrix.CreateTranslation(2.5f * MathF.Sin(seconds * .55f), (seconds * 6) % 4096));
        using var map = drift == null ? null : SKImageFilter.CreateShader(drift);
        using var melt = map == null ? null : SKImageFilter.CreateDisplacementMapEffect(SKColorChannel.R, SKColorChannel.G,
            (float)((1 + strength * .045) * 4 / input.Downscale), map, blur);
        using var paint = new SKPaint { ImageFilter = melt ?? blur };
        surface.Canvas.DrawImage(raw, 0, 0); // opaque edge beneath the displacement gutter
        surface.Canvas.DrawImage(raw, 0, 0, paint);
        return surface.Snapshot();
    }
    internal static double Alpha(int strength) => strength <= 0 ? 0 : .10 + .75 * Math.Pow((Math.Clamp(strength, 1, 100) - 1) / 99.0, .66);
}
