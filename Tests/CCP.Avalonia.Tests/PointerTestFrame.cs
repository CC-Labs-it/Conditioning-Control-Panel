using System;
using System.Diagnostics;
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
