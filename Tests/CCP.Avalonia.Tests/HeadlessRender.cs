using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class HeadlessRender
{
    /// <summary>Renders a frame holding the live tree. One forced tick is not that: while a batch is in
    /// flight MediaContext holds the next commit back, so the tick renders the OLDER batch and the layout
    /// committed after it is missing from the frame. Every tab's first probe ran with one batch pending,
    /// and on a slow runner the stale frame still had no hero there (Linux CI 38316007639: awareness
    /// BtnAwarenessTutorial / ChkAwarenessMaster hit ScrollContentPresenter, pressed False).</summary>
    internal static void Commit(Visual w)
    {
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
