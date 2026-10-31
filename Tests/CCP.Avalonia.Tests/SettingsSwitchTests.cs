using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SettingsSwitchTests
{
    private readonly ITestOutputHelper _output;
    public SettingsSwitchTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public Task TraceSettingsSwitch() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CCP_TRACE_TAB_SWITCH") == "1",
            "set CCP_TRACE_TAB_SWITCH=1 to run the 20-frame benchmark");
        if (Application.Current is null)
            Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var favorites = s.RailFavorites.ToArray();
        var recent = s.RailRecent.ToArray();
        MainShellWindow? w = null;
        try
        {
            s.RailFavorites.Clear();
            s.RailFavorites.AddRange(new[] { "tab.haptics", "door.studio", "door.play" });
            s.RailRecent.Clear();
            w = new MainShellWindow();
            w.Show();
            await PerfBench.NextFrame(w, Stopwatch.StartNew());
            var stages = new Dictionary<string, List<double>>();
            for (int i = 0; i < 21; i++)
            {
                w.ShowTab("progression");
                await PerfBench.NextFrame(w, Stopwatch.StartNew());
                w.TabSwitchMeasured = (tab, costs) =>
                {
                    if (tab != "settings" || i == 0) return;
                    foreach (var cost in costs)
                    {
                        if (!stages.ContainsKey(cost.Key)) stages[cost.Key] = new();
                        stages[cost.Key].Add(cost.Value);
                    }
                };
                var watch = Stopwatch.StartNew();
                w.ShowTab("settings");
                var layoutWatch = Stopwatch.StartNew();
                w.UpdateLayout();
                var layoutMs = layoutWatch.Elapsed.TotalMilliseconds;
                var total = await PerfBench.NextFrame(w, watch);
                if (i > 0)
                {
                    if (!stages.ContainsKey("through-render")) stages["through-render"] = new();
                    stages["through-render"].Add(total);
                    if (!stages.ContainsKey("layout")) stages["layout"] = new();
                    stages["layout"].Add(layoutMs);
                }
                w.TabSwitchMeasured = null;
            }
            foreach (var stage in stages)
                _output.WriteLine($"{stage.Key}: median={PerfBench.Percentile(stage.Value, 50):F3} p95={PerfBench.Percentile(stage.Value, 95):F3} ms");
        }
        finally
        {
            w?.Close();
            s.RailFavorites.Clear(); s.RailFavorites.AddRange(favorites);
            s.RailRecent.Clear(); s.RailRecent.AddRange(recent);
        }
    });
}
