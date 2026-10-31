using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Testing;

/// <summary>Pointer coordinates and compositor hit testing must describe the same settled tree.</summary>
internal static class HeadlessPointerInput
{
    // Keep entrances at their first frame until explicitly settled. A faster/slower runner must
    // not decide whether a pointer test exercises an invisible, translated or finished control.
    internal sealed class PausedTabClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    internal static void RenderLiveTree(MainShellWindow shell, Control? target = null)
    {
        shell.CompleteTabTransition();
        target?.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        // One forced tick may render an older in-flight batch. Await the batch requested after
        // layout, BringIntoView and entrance completion, before sampling or clicking coordinates.
        var batch = ElementComposition.GetElementVisual(shell)!.Compositor.RequestCompositionBatchCommitAsync();
        var deadline = Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the settled tree");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(batch.Rendered.IsCompletedSuccessfully, "the compositor failed to render the settled tree");
    }
}
