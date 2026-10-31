using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Opened alone precedes layout/render. Wait for the render-thread acknowledgement
/// before starting optional desktop work; closing the shell cancels the pending work.</summary>
internal static class AfterFirstFrame
{
    internal static void Schedule(Window window, Action work)
        => Schedule(window, () => { work(); return Task.CompletedTask; });

    internal static void Schedule(Window window, Func<Task> work)
    {
        var closed = false;
        var started = false;
        window.Closed += (_, _) => closed = true;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (started || closed) return;
                started = true; // a tray restore must not create another presence client
                // Boot routing may have hidden the panel in favour of the launcher/game.
                // Observe that interactive window instead, after all Opened handlers return.
                var frameWindow = window.IsVisible ? window :
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                        .Windows.FirstOrDefault(w => w.IsVisible && w is not Views.Windows.SplashScreen);
                if (frameWindow is null)
                {
                    // StartMinimized with a tray host has no window frame to wait for.
                    if (!closed) await work();
                    return;
                }
                var compositor = ElementComposition.GetElementVisual(frameWindow)?.Compositor;
                if (compositor is null) return;
                var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                frameWindow.RequestAnimationFrame(timestamp =>
                {
                    _ = CompleteFrameAsync();
                    async Task CompleteFrameAsync()
                    {
                        try { await compositor.RequestCompositionBatchCommitAsync().Rendered; rendered.TrySetResult(); }
                        catch (Exception ex) { rendered.TrySetException(ex); }
                    }
                });
                await rendered.Task;
                // Yield back to input/layout before the optional work (no arbitrary time delay).
                Dispatcher.UIThread.Post(async () =>
                {
                    if (closed) return;
                    try { await work(); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "After-first-frame startup work failed"); }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "First frame unavailable; optional startup work skipped"); }
        }, DispatcherPriority.Background);
    }
}
