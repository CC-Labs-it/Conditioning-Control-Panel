using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel;

// --perf-bench only. Keep the schema, cycle count and statistics aligned with CCP.Avalonia/PerfBench.cs.
internal static class PerfBench
{
    internal static readonly Stopwatch SinceMain = new();
    internal static readonly string[] TabPanels = { "SettingsTab","PresetsTab","QuestsTab","ProgramsTab","EnhancementsTab","DeeperTab","AchievementsTab","CompanionTab","PlayTab","LeaderboardTab","AssetsTab","DiscordTab","AwarenessTab","RemoteControlTab","AvailableSubjectsTab","BambiTakeoverTab","StudioTab","LockdownTab","BlinkTrainerTab","SheListeningTab","GradedIntakeTab","AppSettingsTab","SpiralTab","ExclusivesTab" };
    private const int Cycles = 20;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

    internal static void PrepareSettings(AppSettings settings)
    {
        // ponytail: a fresh CI sandbox represents an onboarded offline user; no account or session seeding.
        settings.Welcomed = true;
        settings.HasAcceptedAgeVerification = true;
        settings.HasSeenProgramsIntro = true;
        settings.LastSeenVersion = Services.UpdateService.AppVersion;
        settings.OfflineMode = true;
    }

    internal static async Task Run(MainWindow window, string outPath, string[] tabs)
    {
        var code = 1;
        try
        {
            File.Delete(outPath); // a failed repeat must never leave a successful stale report
            var firstFrame = NextFrame(window, SinceMain);
            window.Show();
            var startupMs = await firstFrame;
            using var process = Process.GetCurrentProcess();
            var sinceProcessMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
            if (tabs.Length == 0) throw new InvalidOperationException("no benchmark tabs");
            var samples = tabs.Select(_ => new List<double>()).ToArray();
            var clock = new Stopwatch();
            for (var cycle = 0; cycle <= Cycles; cycle++) // discard cycle 0 (warm-up)
                for (var i = 0; i < tabs.Length; i++)
                {
                    clock.Restart();
                    if (!window.ShowPerfBenchTab(tabs[i]))
                    {
                        Console.Error.WriteLine($"perf-bench: tab {tabs[i]} is missing or did not show");
                        Exit(3);
                        return;
                    }
                    var ms = await NextFrame(window, clock);
                    if (cycle > 0) samples[i].Add(ms);
                }
            for (var i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            process.Refresh();
            var json = JsonSerializer.Serialize(new
            {
                schema = "ccp-perf-bench/1", head = "wpf",
                os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                startup_ms = Math.Round(startupMs, 1),
                startup_since_process_ms = Math.Round(sinceProcessMs, 1),
                working_set_mb = Math.Round(process.WorkingSet64 / 1048576.0, 1), cycles = Cycles,
                tabs = tabs.Select((tab, i) => new
                {
                    tab, median_ms = Math.Round(Percentile(samples[i], 50), 2),
                    p95_ms = Math.Round(Percentile(samples[i], 95), 2),
                }).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outPath, json);
            Console.WriteLine(json);
            code = 0;
        }
        catch (TimeoutException) { Console.Error.WriteLine("perf-bench: frame timeout"); code = 4; }
        catch (Exception ex) { Console.Error.WriteLine("perf-bench: " + ex); }
        // App.OnExit hard-terminates with 0; preserve benchmark failure codes by exiting here.
        Exit(code);
    }

    // Not Environment.Exit: in WPF its ProcessExit teardown can crash with 0xE0434352 and lose the code
    // (see App.OnExit). Flush, then hard-terminate with the benchmark's own code.
    internal static void Exit(int code)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(), (uint)code);
        Environment.Exit(code); // unreachable fallback if TerminateProcess is refused
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    private static async Task<double> NextFrame(Window window, Stopwatch clock)
    {
        var done = new TaskCompletionSource();
        EventHandler handler = (_, _) => done.TrySetResult();
        CompositionTarget.Rendering += handler;
        try
        {
            window.InvalidateVisual();
            await done.Task.WaitAsync(FrameTimeout);
            // Rendering fires before drawing. Let that Render-priority pass finish before sampling.
            return await window.Dispatcher.InvokeAsync(() => clock.Elapsed.TotalMilliseconds,
                DispatcherPriority.Background);
        }
        finally { CompositionTarget.Rendering -= handler; }
    }

    internal static double Percentile(List<double> values, int percentile)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (percentile == 50)
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return sorted[Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)];
    }
}

public partial class MainWindow
{
    internal bool ShowPerfBenchTab(string panel)
    {
        if (!PerfBench.TabPanels.Contains(panel)) return false;
        var key = panel[..^3].ToLowerInvariant();
        ShowTab(key); // same path as rail buttons, including legacy redirects
        // Exclusives is now a lazy lane page named Premium in WPF. Keep the shared JSON key.
        return panel == "ExclusivesTab"
            ? _navTabViews.TryGetValue("premium", out var premium) && premium.IsVisible
            : FindName(panel) is FrameworkElement { IsVisible: true };
    }
}
