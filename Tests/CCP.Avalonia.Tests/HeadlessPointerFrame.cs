using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class HeadlessPointerFrame
{
    // Keep entrance time fixed until Render advances the actual transition. A slow runner
    // must not accidentally make the pointer test pass by letting 620 ms elapse.
    internal sealed class FrozenEntranceClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    /// <summary>Renders a frame holding the live tree. One forced tick is not that: while a batch is in
    /// flight MediaContext holds the next commit back, so the tick renders the OLDER batch and the layout
    /// committed after it is missing from the frame. Every tab's first probe ran with one batch pending,
    /// and on a slow runner the stale frame still had no hero there (Linux CI 38316007639: awareness
    /// BtnAwarenessTutorial / ChkAwarenessMaster hit ScrollContentPresenter, pressed False).</summary>
    internal static void Render(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        // A committed frame can still contain the entrance's moving/transparent cards.
        // Advance the real finite transition to its end before sampling hit geometry (P08).
        if (w is MainShellWindow shell) shell.CompleteTabTransition();
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
    }

}
