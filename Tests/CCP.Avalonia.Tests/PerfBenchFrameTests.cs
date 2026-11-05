using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PerfBenchFrameTests
{
    /// <summary>Inside a frame callback and outside one, the awaited frame must already carry the layout
    /// and pixels. Only the inside case catches the old extra animation pulse; the production
    /// --perf-bench path is proven by the perf-bench-windows numbers, not here.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NextFrameIncludesLayoutAndPixelsWithoutAnExtraAnimationPulse(bool insideFrameCallback) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var fill = new Border { Background = Brushes.Red, Width = 10, Height = 40,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
        var w = new Window { Width = 80, Height = 60, Content = fill, Background = Brushes.Black };
        var commits = 0;
        Action committed = () => commits++;
        EventInfo? afterCommit = null;
        Compositor? compositor = null;
        try
        {
            w.Show();
            await PerfBench.NextFrame(w, Stopwatch.StartNew());
            compositor = ElementComposition.GetElementVisual(w)!.Compositor;
            // Avalonia 12.1.2 has no public commit counter. Observe the real compositor rather
            // than substituting a fake frame provider (which could miss the extra pulse).
            afterCommit = typeof(Compositor).GetEvent("AfterCommit", BindingFlags.Instance | BindingFlags.NonPublic)!;
            afterCommit.GetAddMethod(true)!.Invoke(compositor, new object[] { committed });
            Task<double> Change()
            {
                commits = 0;
                fill.Width = 50; // requires layout; green at x=30 cannot be the previous frame
                fill.Background = Brushes.Lime;
                return PerfBench.NextFrame(w, Stopwatch.StartNew());
            }
            if (insideFrameCallback)
            {
                var started = new TaskCompletionSource<Task<double>>();
                w.RequestAnimationFrame(_ => started.SetResult(Change()));
                await await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            else
                await Change();
            Assert.Equal(1, commits);
            Assert.Equal(50, fill.Bounds.Width);

            // CaptureRenderedFrame forces MORE frames and would conceal an early completion.
            // Read the headless platform's already-rendered bitmap without pumping anything.
            var platform = w.PlatformImpl!;
            var getFrame = platform.GetType().GetMethod("GetLastRenderedFrame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            using var frame = Assert.IsType<WriteableBitmap>(getFrame.Invoke(platform, null));
            using var stream = new MemoryStream();
            frame.Save(stream);
            stream.Position = 0;
            using var pixels = SKBitmap.Decode(stream);
            Assert.Equal(SKColors.Lime, pixels.GetPixel(30, 30));
            Assert.Equal(SKColors.Black, pixels.GetPixel(60, 30));
        }
        finally
        {
            if (afterCommit != null) afterCommit.GetRemoveMethod(true)!.Invoke(compositor, new object[] { committed });
            w.Close();
        }
    });
}
