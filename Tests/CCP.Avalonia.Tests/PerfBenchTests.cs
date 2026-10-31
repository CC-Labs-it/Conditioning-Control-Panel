using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
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
    public Task HiddenCataloguesDoNotLoadArtAtStartup() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            var vault = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var studio = shell.Named<StudioTabView>("StudioTab")!;
            Assert.False(vault.CatalogueBuilt);
            Assert.False(studio.ArtLoaded);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            // Loaded fires for hidden tabs too. It must not pull their catalogue/art into Home's frame.
            Assert.False(vault.CatalogueBuilt);
            Assert.False(studio.ArtLoaded);
            Assert.Empty(vault.FindControl<ItemsControl>("ExclusivesShelf")!.Items);
            Assert.True(PerfBench.Show(shell, "ExclusivesTab"));
            Assert.True(vault.CatalogueBuilt);
            Assert.NotEmpty(vault.FindControl<ItemsControl>("ExclusivesShelf")!.Items);
            Assert.True(PerfBench.Show(shell, "StudioTab"));
            Assert.True(studio.ArtLoaded);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EveryNavTabShowsAndAMissingTabIsReported() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            Program.BuildAvaloniaApp()
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
