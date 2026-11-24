using SkiaSharp;

namespace ConditioningControlPanel;

/// <summary>
/// The image sampling each old <c>SKFilterQuality</c> meant. SkiaSharp 4 removed SKFilterQuality and
/// SKPaint.FilterQuality (https://mono.github.io/SkiaSharp/docs/releases/4.148.0.html: remaining pre-v4
/// obsolete members trimmed); the paint-only DrawImage/DrawBitmap overloads now sample nearest, so the
/// quality goes on the draw call. Values are SkiaSharp's own SKFilterQuality.ToSamplingOptions mapping
/// (https://github.com/mono/SkiaSharp/blob/main/binding/SkiaSharp/SKPaint.cs).
/// </summary>
public static class SkSampling
{
    public static readonly SKSamplingOptions Low = new(SKFilterMode.Linear, SKMipmapMode.None);
    public static readonly SKSamplingOptions Medium = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    public static readonly SKSamplingOptions High = new(SKCubicResampler.Mitchell);
}
