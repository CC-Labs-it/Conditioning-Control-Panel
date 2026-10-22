using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>--perf-bench (PerfBench.cs): a tab that is missing must fail the run, never time an empty switch.
/// The exit code itself is proven by the perf-bench-linux CI job (--perf-bench-tabs SettingsTab,NoSuchTab exits 3).</summary>
public sealed class PerfBenchTests
{
    [Fact]
    public Task EveryNavTabShowsAndAMissingTabIsReported() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            foreach (var tab in NavCheck.TabPanels)
                Assert.True(PerfBench.Show(shell, tab), tab);
            Assert.False(PerfBench.Show(shell, "NoSuchTab"));
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void MedianInterpolatesAndP95IsNearestRank()
    {
        var xs = new List<double>();
        for (var i = 1; i <= 20; i++) xs.Add(i);
        Assert.Equal(10.5, PerfBench.Percentile(xs, 50));
        Assert.Equal(19, PerfBench.Percentile(xs, 95));
    }
}
