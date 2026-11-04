using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class PointerFrames
{
    /// <summary>Finish the finite entrance before sampling pointer geometry, then render that tree.
    /// A commit alone can leave a later animation callback moving the managed tree after its frame.</summary>
    internal static void Render(MainShellWindow w)
    {
        w.CompleteTabTransition();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var batch = ElementComposition.GetElementVisual(w)!.Compositor.RequestCompositionBatchCommitAsync();
        var deadline = Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the live tree");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        batch.Rendered.GetAwaiter().GetResult();
    }
}
