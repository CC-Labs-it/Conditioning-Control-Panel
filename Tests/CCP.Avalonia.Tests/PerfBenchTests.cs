using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>--perf-bench (PerfBench.cs): a tab that is missing must fail the run, never time an empty switch.
/// The exit code itself is proven by the perf-bench-linux CI job (--perf-bench-tabs SettingsTab,NoSuchTab exits 3).</summary>
public sealed class PerfBenchTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task EveryNavTabShowsAndAMissingTabIsReported() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
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

    /// <summary>Both stop points of a tab switch arrive on the same clock (the parity rule reads the handoff).</summary>
    [Fact]
    public Task NextFrameReportsTheHandoffAndTheDrawnFrame() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Assert.True(PerfBench.Show(shell, "SettingsTab"));
            var clock = Stopwatch.StartNew();
            var frame = PerfBench.NextFrame(shell, clock);
            for (var i = 0; i < 500 && !frame.IsCompleted; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }
            Assert.True(frame.IsCompleted, "the next frame never arrived under headless");   // GetResult would block the UI thread forever
            var (handoff, drawn) = frame.GetAwaiter().GetResult();
            Assert.InRange(handoff, 0, clock.Elapsed.TotalMilliseconds);
            Assert.InRange(drawn, 0, clock.Elapsed.TotalMilliseconds);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>ccp-perf-bench/1 stays backward compatible: median_ms/p95_ms keep the drawn numbers, handoff_* is added.</summary>
    [Fact]
    public void TabRowKeepsDrawnAsMedianAndAddsTheHandoff()
    {
        var row = JsonSerializer.SerializeToElement(PerfBench.TabRow("SettingsTab",
            new List<double> { 20, 30, 25 }, new List<double> { 80, 100, 90 }));
        Assert.Equal("SettingsTab", row.GetProperty("tab").GetString());
        Assert.Equal(90, row.GetProperty("median_ms").GetDouble());
        Assert.Equal(100, row.GetProperty("p95_ms").GetDouble());
        Assert.Equal(25, row.GetProperty("handoff_median_ms").GetDouble());
        Assert.Equal(30, row.GetProperty("handoff_p95_ms").GetDouble());
    }

    [Fact]
    public void MedianInterpolatesAndP95IsNearestRank()
    {
        var xs = new List<double>();
        for (var i = 1; i <= 20; i++) xs.Add(i);
        Assert.Equal(10.5, PerfBench.Percentile(xs, 50));
        Assert.Equal(19, PerfBench.Percentile(xs, 95));
    }
}
