using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;
public sealed class EnhancementAnalyticsTabTests
{
    private static string?[] Texts(Control c) => c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    [Fact]
    public Task OwnedProPanelsBuildLazilyViaKeyboardAndShowLiveAndHistoricAnalytics() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current is null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder);
        var oldSettings = CoreSettings.ServiceProvider;
        var achievements = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldEngine = achievements.GetValue(null);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? window = null;
        var dir = Directory.CreateTempSubdirectory("ccp-enhancement-analytics-").FullName;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            achievements.SetValue(null, engine);
            engine.Progress.TotalXPEarned = 1234;
            engine.Progress.LifetimeSkillPointsSpent = 210;
            engine.Progress.LongestSessionMinutes = 75;
            var s = CoreSettings.Current;
            s.MotionLevel = MotionLevel.Off;
            s.UnlockedSkills = new List<string> { "ditzy_data", "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo" };
            s.SeasonStatsSeason = "2035-02";
            s.SeasonConditioningMinutes = 150;
            s.SeasonPeakRank = 3;
            s.SeasonPeakRankTotal = 100;
            s.PlayerLevel = 50;
            s.SeasonPeakLevel = 30;
            s.SeasonPointsSpent = 11;
            s.SeasonActiveDays = new List<string> { "2035-02-01" };
            s.SeasonFeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 3 };
            var tab = new EnhancementsTabView();
            window = new Window { Width = 1400, Height = 700, Content = tab };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var panels = tab.GetLogicalDescendants().OfType<Border>()
                .Where(b => b.Name?.StartsWith("ProPanel_") == true).ToDictionary(b => b.Name!);
            Assert.Equal(4, panels.Count);
            Assert.All(panels.Values, p => { Assert.False(p.IsVisible); Assert.Null(p.Child); });
            // Written after construction: first expansion must read it, not a cached eager snapshot.
            var snapshot = new SeasonRecapSnapshot { SeasonKey = "2035-01", Schema = 1, SeasonMinutes = 300,
                PeakLevel = 40, PeakRank = 1, DaysActive = 2, PointsSpentSeason = 7,
                ActiveDays = new List<string> { "2035-01-01" },
                FeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 7, [SeasonFeatureKeys.Video] = 2 } };
            SeasonRecapStore.Save(snapshot);
            foreach (var id in new[] { "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report" })
            {
                var toggle = tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ProToggle_" + id);
                toggle.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                var panel = panels["ProPanel_" + id];
                Assert.True(panel.IsVisible);
                Assert.NotNull(panel.Child);
                var built = panel.Child;
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Assert.False(panel.IsVisible);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Same(built, panel.Child);
                if (Environment.GetEnvironmentVariable("CCP_ANALYTICS_EVIDENCE_DIR") is { } evidence)
                {
                    Directory.CreateDirectory(evidence);
                    toggle.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(evidence, "analytics-" + id + ".png"));
                }
            }
            var lifetime = panels["ProPanel_ditzy_data_pro"];
            Assert.Contains("1,234", Texts(lifetime));
            Assert.Contains(Loc.Get("label_heatmap_title"), Texts(lifetime));
            var cells = lifetime.GetLogicalDescendants().OfType<Border>().Where(b => b.Tag is string date && date.StartsWith("2035-01-")).ToArray();
            Assert.Equal(31, cells.Length);
            Assert.Equal("2035-01-01", ToolTip.GetTip(cells[0]));
            Assert.Equal(Colors.HotPink, ((ISolidColorBrush)cells[0].Background!).Color);
            Assert.Equal(Color.FromRgb(38, 38, 64), ((ISolidColorBrush)cells[1].Background!).Color);
            var rewind = panels["ProPanel_season_rewind"];
            Assert.Contains(Loc.Get("label_rewind_spent"), Texts(rewind));
            Assert.Contains("2035-02 •", Texts(rewind));
            Assert.Contains("2035-01", Texts(rewind));
            Assert.Contains("11", Texts(rewind));
            Assert.Contains(rewind.GetLogicalDescendants().OfType<ConditioningControlPanel.Avalonia.Controls.IconGlyph>(),
                g => g.Kind == FluentIcons.Common.Symbol.ChevronDown);   // WPF " ▼": the season time fell
            var table = rewind.GetLogicalDescendants().OfType<Grid>().Single(g => g.ColumnDefinitions.Count == 8);
            Assert.Equal("—", ((TextBlock)table.Children.Single(c => Grid.GetRow(c) == 2 && Grid.GetColumn(c) == 6)).Text);
            Assert.Equal("—", ((TextBlock)table.Children.Single(c => Grid.GetRow(c) == 2 && Grid.GetColumn(c) == 7)).Text);
            var records = Texts(panels["ProPanel_bestie_records"]);
            Assert.Contains("#1", records);
            Assert.Contains("  2035-01", records);
            Assert.Contains("50", records);
            var drain = panels["ProPanel_brain_drain_report"];
            Assert.Contains("3 / 10", Texts(drain));
            Assert.Contains("0 / 2", Texts(drain));
            Assert.Contains(drain.GetLogicalDescendants().OfType<Border>(), b => b.Height == 5 && b.Width == 168);
            // Ownership changes rebuild the header: unowned sections disappear, rather than showing locked analytics.
            s.UnlockedSkills = new List<string> { "ditzy_data" };
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(tab.GetLogicalDescendants().OfType<Border>(), b => b.Name?.StartsWith("ProToggle_") == true);
        }
        finally
        {
            window?.Close();
            var snapshotPath = Path.Combine(CorePaths.UserData, "season-recaps", "2035-01.json");
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
            Directory.Delete(dir, true);
            service.SaveImmediate();
            achievements.SetValue(null, oldEngine);
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}
