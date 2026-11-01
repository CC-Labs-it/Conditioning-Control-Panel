using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class PointerTestFrame
{
    // Hold the entrance at its start until the pointer fixture explicitly completes it.
    internal sealed class Clock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    // A forced timer tick may service an older compositor batch without delivering this frame.
    internal static void Next(TopLevel top)
    {
        var delivered = false;
        top.RequestAnimationFrame(_ => delivered = true);
        var deadline = Stopwatch.StartNew();
        while (!delivered)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the animation frame was never delivered");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    internal static void Render(MainShellWindow shell)
    {
        Dispatcher.UIThread.RunJobs();
        // Finish the real finite entrance. Turning motion off would hide its geometry bugs.
        shell.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(shell.EnteringTab);
        // InputHitTest reads the last rendered frame, not the live layout after BringIntoView.
        var batch = ElementComposition.GetElementVisual(shell)!.Compositor.RequestCompositionBatchCommitAsync();
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
