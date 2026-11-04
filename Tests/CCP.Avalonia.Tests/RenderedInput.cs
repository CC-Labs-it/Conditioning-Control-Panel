using System;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Xunit;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace CCP.Avalonia.Tests;

internal static class RenderedInput
{
    // Model a runner that has not delivered any entrance-animation frames yet.
    internal sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    // InputHitTest reads the compositor's last rendered geometry. Finish pending layout/scroll
    // work and wait for its batch before sampling coordinates or sending pointer input.
    internal static void Render(MainShellWindow root)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.Null(root.EnteringTab); // never accept input geometry from an unfinished entrance
        root.UpdateLayout();
        var batch = ElementComposition.GetElementVisual(root)!.Compositor.RequestCompositionBatchCommitAsync();
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
