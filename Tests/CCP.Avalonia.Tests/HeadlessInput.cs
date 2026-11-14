using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class HeadlessInput
{
    // Headless pointer input reads the compositor's rendered scene, not UpdateLayout's live tree.
    // A single timer tick can render an older batch while the current commit is throttled.
    internal static void RenderLiveTree(Visual root)
    {
        Dispatcher.UIThread.RunJobs();
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

    // A forced render tick may be throttled while an older batch is in flight.
    internal static void RenderAnimationFrame(TopLevel root, Action? pump = null)
    {
        // A regression can suppress one pump to replay a throttled frame without sleeping.
        pump ??= () => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); };
        bool rendered = false;
        root.RequestAnimationFrame(_ => rendered = true);
        var deadline = Stopwatch.StartNew();
        while (!rendered)
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the requested animation frame never ran");
            pump();
        }
    }

    internal static (bool Hit, bool Pressed, Visual? Target) PressAt(Button button, Window window, Point at)
    {
        var hit = window.InputHitTest(at) as Visual;
        // Pressed/hover styles may replace content, detaching this visual during the input.
        bool hitButton = hit == button || (hit is not null && button.IsVisualAncestorOf(hit));
        bool pressed = false;
        void Swallow(object? sender, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
        button.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
        try
        {
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            return (hitButton, pressed, hit);
        }
        finally { button.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Swallow); }
    }
}
