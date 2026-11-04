using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Pointer coordinates and hit testing must describe the same rendered tree.</summary>
internal static class HeadlessPointerFrame
{
    internal static void Render(MainShellWindow shell)
    {
        Dispatcher.UIThread.RunJobs();
        // Finish the real finite entrance, as the other shell pointer tests do. Otherwise a
        // frame tick can move a card between its rendered hit region and TranslatePoint.
        shell.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
        // BringIntoView and layout are not a rendered frame. Await the requested batch itself:
        // one forced tick can still render an older batch while the new one is in flight.
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
