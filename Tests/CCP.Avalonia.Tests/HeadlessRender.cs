using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class HeadlessRender
{
    /// <summary>A completed batch can queue more UI layout work. Drain that batch first, then
    /// arrange and render a fresh one. If layout changes while rendering, its coordinates belong
    /// to a newer scene, so commit them before probing. No hit-test result is used as a wait condition.</summary>
    internal static void WaitForLiveTree(Window window)
    {
        var compositor = ElementComposition.GetElementVisual(window)!.Compositor;
        var deadline = Stopwatch.StartNew();
        void RenderBatch()
        {
            var batch = compositor.RequestCompositionBatchCommitAsync();
            while (!batch.Rendered.IsCompleted)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the live tree");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
        }

        Dispatcher.UIThread.RunJobs();
        RenderBatch();
        bool layoutChanged = false;
        void LayoutChanged(object? sender, EventArgs e) => layoutChanged = true;
        window.LayoutUpdated += LayoutChanged;
        try
        {
            do
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "layout did not settle before hit testing");
                window.UpdateLayout();
                layoutChanged = false;
                RenderBatch();
            } while (layoutChanged);
        }
        finally { window.LayoutUpdated -= LayoutChanged; }
    }

}
