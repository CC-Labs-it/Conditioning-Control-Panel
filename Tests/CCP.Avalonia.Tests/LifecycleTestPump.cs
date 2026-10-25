using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

internal static class LifecycleTestPump
{
    internal static void Setup()
    {
        if (Application.Current is null)
        {
            var options = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            // Lifecycle/curve tests use the bundled face for Windows-only font names on Linux.
            var mappings = new Dictionary<string, FontFamily>(options.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            options.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(options)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }

    // Frame ticks request work; they neither advance the animation clock nor guarantee that
    // compositor/finalizer continuations have returned. Yield the UI thread between probes.
    // The deadline is a hang guard, not a delay or an assertion tolerance: return on completion.
    internal static async Task UntilAsync(Func<bool> done, string failure, bool collect = false)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (collect)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            if (done()) return;
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), failure);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
