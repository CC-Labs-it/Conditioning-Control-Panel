using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class HeadlessInput
{
    // Pointer probes must not rely on wall time finishing the entrance before a click.
    internal static readonly TimeProvider Clock = new FrozenClock();
    private sealed class FrozenClock : TimeProvider { public override long GetTimestamp() => 0; }

    internal static void RenderSettled(MainShellWindow shell)
    {
        // Settle the real finite entrance (P08), then render its final geometry. RunJobs alone
        // does not publish a compositor hit-test frame, including after BringIntoView scrolls.
        shell.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
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
