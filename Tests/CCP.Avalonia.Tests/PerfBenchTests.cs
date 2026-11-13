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
            shell.Hide();
            vault.RefreshVault();
            studio.RepaintModAwareChrome();
            Assert.False(vault.CatalogueBuilt);
            Assert.False(studio.ArtLoaded);
            shell.Show();
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
    public Task FirstPremiumVisitBuildsTheCatalogueOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var shelf = shell.Named<ExclusivesTabView>("ExclusivesTab")!.FindControl<ItemsControl>("ExclusivesShelf")!;
            var builds = 0;
            shelf.PropertyChanged += (_, e) => { if (e.Property == ItemsControl.ItemsSourceProperty) builds++; };
            Assert.True(PerfBench.Show(shell, "ExclusivesTab"));
            Dispatcher.UIThread.RunJobs(); // Include the queued visibility pass, not just ShowTab.
            Assert.Equal(1, builds);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BuiltPremiumRepaintsTierAndArtWhileShellIsHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var (premium, free) = (global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider,
            global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider);
        var shell = new MainShellWindow();
        try
        {
            global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => false;
            global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider = _ => false;
            shell.Show();
            PerfBench.Show(shell, "ExclusivesTab");
            Dispatcher.UIThread.RunJobs();
            var vault = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var veil = vault.FindControl<Border>("SpotVeil")!;
            var art = vault.FindControl<Image>("SpotArtImage")!;
            Assert.True(veil.IsVisible);
            var before = art.Source;
            Assert.NotNull(before);
            shell.Hide();
            Assert.True(vault.IsVisible);
            Assert.False(vault.IsEffectivelyVisible);
            global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
            shell.RefreshExclusivesTab(); // Tier/day rollover's production repaint entry point.
            Assert.False(veil.IsVisible);
            Assert.NotSame(before, art.Source);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(veil.IsVisible); // No tab switch is required after tray restore.
        }
        finally
        {
            shell.Close();
            (global::ConditioningControlPanel.CoreEntitlement.HasPremiumProvider,
                global::ConditioningControlPanel.CoreEntitlement.IsFreeTodayProvider) = (premium, free);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BuiltStudioRepaintsArtWhileShellIsHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            PerfBench.Show(shell, "StudioTab");
            Dispatcher.UIThread.RunJobs();
            var studio = shell.Named<StudioTabView>("StudioTab")!;
            var art = studio.FindControl<Image>("ImgStudioDoorArt")!;
            var before = art.Source;
            Assert.NotNull(before);
            shell.Hide();
            Assert.True(studio.IsVisible);
            studio.RepaintModAwareChrome(); // ModChanged's production repaint entry point.
            Assert.True(studio.ArtLoaded);
            Assert.NotSame(before, art.Source);
            shell.Show();
            Assert.True(studio.ArtLoaded);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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
