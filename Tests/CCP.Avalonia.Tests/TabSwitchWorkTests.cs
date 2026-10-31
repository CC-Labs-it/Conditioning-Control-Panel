using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabSwitchWorkTests
{
    [Fact]
    public void RepeatShowKeepsVisuals() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            for (var cycle = 0; cycle < 21; cycle++)
                foreach (var name in new[] { "ProgramsTab", "BlinkTrainerTab" })
                {
                    Assert.True(PerfBench.Show(shell, name));
                    using var trace = ConditioningControlPanel.Avalonia.Helpers.TabWorkTrace.Start(name);
                    shell.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    trace?.Mark("layout-jobs");
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = shell.CaptureRenderedFrame();
                    trace?.Mark("render");
                }
        }
        finally { shell.Close(); }
    });
}
