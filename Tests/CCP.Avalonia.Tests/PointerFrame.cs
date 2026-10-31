using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Pointer coordinates and compositor hit testing must describe the same settled frame.</summary>
internal static class PointerFrame
{
    // A slow runner must not finish an entrance by accident while a pointer test measures it.
    internal sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    internal static void RenderLiveTree(MainShellWindow shell)
    {
        Dispatcher.UIThread.RunJobs();
        shell.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(shell.EnteringTab);
        // BringIntoView and layout update the UI tree before the compositor hit tree.
        // Await this batch, including any older batch already in flight.
        var batch = ElementComposition.GetElementVisual(shell)!.Compositor.RequestCompositionBatchCommitAsync();
        var deadline = Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never rendered the live tree");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
