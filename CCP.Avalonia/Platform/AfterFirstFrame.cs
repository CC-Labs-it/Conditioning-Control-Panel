using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Wait for a live interactive surface to render. A disappearing boot surface
/// is reselected; closing the shell cancels pending work. Tray restores never start it twice.</summary>
internal static class AfterFirstFrame
{
    internal static void Schedule(Window shell, Action work)
        => Schedule(shell, () => { work(); return Task.CompletedTask; });

    internal static void Schedule(Window shell, Func<Task> work) => _ = Schedule(shell, work,
        () => shell.IsVisible ? shell :
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .Windows.FirstOrDefault(w => w.IsVisible && w is not Views.Windows.SplashScreen), NextFrame);

    // Tests substitute the render acknowledgement, not the window events or dispatcher.
    internal static Task Schedule(Window shell, Func<Task> work,
        Func<Window?> selectSurface, Func<Window, Task> nextFrame)
    {
        var closed = Signal();
        var finished = Signal();
        var started = false;
        void OnClosed(object? sender, EventArgs e)
        {
            closed.TrySetResult();
            if (!started) Finish();
        }
        void Finish()
        {
            shell.Opened -= OnOpened;
            shell.Closed -= OnClosed;
            finished.TrySetResult();
        }
        void OnOpened(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () =>
        {
            if (started || closed.Task.IsCompleted) return;
            started = true;
            shell.Opened -= OnOpened;
            try
            {
                // Opened routing may hide the panel in favour of a launcher/game. If that
                // surface disappears before rendering, observe its replacement instead.
                while (!closed.Task.IsCompleted && selectSurface() is { } surface)
                {
                    try { if (await RenderOrDisappear(surface, closed.Task, nextFrame)) break; }
                    catch (Exception ex)
                    {
                        // No usable renderer: optional services still start, as for tray-only boot.
                        Serilog.Log.Debug(ex, "First frame unavailable; using no-frame startup fallback");
                        break;
                    }
                }
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!closed.Task.IsCompleted) await work();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "After-first-frame startup work failed"); }
            finally { Finish(); }
        }, DispatcherPriority.Background);
        shell.Opened += OnOpened;
        shell.Closed += OnClosed;
        return finished.Task;
    }

    private static async Task<bool> RenderOrDisappear(Window surface, Task closed, Func<Window, Task> nextFrame)
    {
        var gone = Signal();
        void Disappeared(object? sender, EventArgs e) => gone.TrySetResult();
        void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty && !surface.IsVisible) gone.TrySetResult();
        }
        surface.Closed += Disappeared;
        surface.PropertyChanged += VisibilityChanged;
        try
        {
            if (!surface.IsVisible) return false;
            var frame = nextFrame(surface);
            await Task.WhenAny(frame, gone.Task, closed);
            if (closed.IsCompleted || gone.Task.IsCompleted || !surface.IsVisible) return false;
            await frame;
            return true;
        }
        finally
        {
            surface.Closed -= Disappeared;
            surface.PropertyChanged -= VisibilityChanged;
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task NextFrame(Window window)
    {
        var compositor = ElementComposition.GetElementVisual(window)?.Compositor;
        if (compositor is null) return Task.CompletedTask;
        var rendered = Signal();
        window.RequestAnimationFrame(timestamp =>
        {
            _ = CompleteFrameAsync();
            async Task CompleteFrameAsync()
            {
                try { await compositor.RequestCompositionBatchCommitAsync().Rendered; rendered.TrySetResult(); }
                catch (Exception ex) { rendered.TrySetException(ex); }
            }
        });
        return rendered.Task;
    }
}
