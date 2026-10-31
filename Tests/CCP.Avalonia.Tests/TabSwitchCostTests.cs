using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabSwitchCostTests(ITestOutputHelper output)
{
    [Fact]
    public Task BenchmarkSwitchStages() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        // 24 tabs x 21 cycles of real frames (minutes): a benchmark, not a regression check.
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH") == "1", "set CCP_TRACE_TAB_SWITCH=1 to run");
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();   // P52/P76: no intro may open mid-run
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow();
        var samples = new Dictionary<string, Dictionary<string, List<double>>>();
        var cycle = 0;
        shell.TabSwitchMeasured = (tab, stages) =>
        {
            if (cycle == 0 || !stages.ContainsKey("visibility")) return;
            foreach (var (stage, ms) in stages) Add(tab, stage, ms);
        };
        void Add(string tab, string stage, double ms)
        {
            if (!samples.TryGetValue(tab, out var row)) samples[tab] = row = new();
            if (!row.TryGetValue(stage, out var values)) row[stage] = values = new();
            values.Add(ms);
        }
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            // Identical order and warm-up count to PerfBench, including the section changes
            // preceding the five reported same-section cases. No latency assertions on shared CI.
            for (cycle = 0; cycle <= PerfBench.Cycles; cycle++)
                foreach (var panel in NavCheck.TabPanels)
                {
                    Assert.True(PerfBench.Show(shell, panel), panel);
                    var clock = Stopwatch.StartNew();
                    shell.UpdateLayout();
                    if (cycle > 0) Add(shell.CurrentTab, "layout", clock.Elapsed.TotalMilliseconds);
                    clock.Restart();
                    var renderedMs = await PerfBench.NextFrame(shell, clock);
                    if (cycle > 0) Add(shell.CurrentTab, "next-frame", renderedMs);
                }
            foreach (var (tab, stages) in samples)
                output.WriteLine(tab + ": " + string.Join(", ", stages.Select(s =>
                    $"{s.Key}={PerfBench.Percentile(s.Value, 50):F3}ms")));
            Assert.Equal(NavCheck.TabPanels.Length, samples.Count);
            Assert.All(samples.Values, stages => Assert.All(stages.Values, xs => Assert.Equal(PerfBench.Cycles, xs.Count)));
        }
        finally { shell.TabSwitchMeasured = null; shell.Close(); StartupLadder.ResetForTests(); CoreSettings.ServiceProvider = null; }
    });
}
