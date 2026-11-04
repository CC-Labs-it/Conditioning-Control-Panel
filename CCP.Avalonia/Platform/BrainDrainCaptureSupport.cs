// Compatibility for the unchanged WPF capture pump and SkiaSharp 3.
global using System;
global using System.Collections.Generic;
global using System.Linq;

namespace SkiaSharp
{
    internal static class BrainDrainSkiaCompat
    {
        extension(SKImageFilter)
        {
            internal static SKImageFilter CreatePaint(SKPaint paint) => SKImageFilter.CreateShader(paint.Shader);
        }
    }
}
namespace ConditioningControlPanel.Services.Compositor
{
    internal static class App { internal static Serilog.ILogger Logger => Serilog.Log.Logger; }
    internal static class VideoDiag { internal static void Log(string tag, string message) => Serilog.Log.Debug("{Tag}: {Message}", tag, message); }
}
