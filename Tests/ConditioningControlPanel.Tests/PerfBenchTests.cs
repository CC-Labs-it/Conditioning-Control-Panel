using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class PerfBenchTests
{
    [Fact]
    public void StatisticsMatchAvaloniaMedianAndNearestRankP95()
    {
        var samples = Enumerable.Range(1, 20).Select(i => (double)i).Reverse().ToList();
        Assert.Equal(10.5, PerfBench.Percentile(samples, 50));
        Assert.Equal(19, PerfBench.Percentile(samples, 95));
        Assert.Equal(3, PerfBench.Percentile(new List<double> { 5, 1, 3 }, 50));
    }

    [Fact]
    public void WpfMeasuresEverySharedAvaloniaTabInTheSameOrder()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Avalonia"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "CCP.Avalonia", "NavCheck.cs"));
        var list = Regex.Match(source, @"TabPanels = \{([^}]+)\}").Groups[1].Value;
        var tabs = Regex.Matches(list, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(tabs);
        Assert.Equal(tabs, PerfBench.TabPanels);
    }

    [Fact]
    public void SandboxPreparationSuppressesModalOnboarding()
    {
        var settings = new AppSettings();
        PerfBench.PrepareSettings(settings);
        Assert.True(settings.Welcomed && settings.HasAcceptedAgeVerification);
        Assert.True(settings.HasSeenProgramsIntro && settings.OfflineMode);
        Assert.Equal(Services.UpdateService.AppVersion, settings.LastSeenVersion);
    }
}
