using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class EnhancementsProTests
{
    private static readonly string[] Skills = { "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report" };
    private static T[] All<T>(Control c) where T : Control => c.GetLogicalDescendants().OfType<T>().ToArray();
    private static string?[] Texts(Control c) => All<TextBlock>(c).Select(t => t.Text).ToArray();
    private static void Toggle(Control c, Key key = Key.Enter) => c.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static Border Open(EnhancementsTabView tab, string id)
    {
        Toggle(All<Border>(tab).Single(b => b.Name == "ProToggle_" + id));
        return All<Border>(tab).Single(b => b.Name == "ProPanel_" + id);
    }

    private static Task OnUi(Action<EnhancementsTabView, AppSettings, AchievementProgress, Window> test, string[]? skills = null) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var old = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var recaps = Path.Combine(CorePaths.UserData, "season-recaps");
            var backup = recaps + "." + Guid.NewGuid().ToString("N");
            if (Directory.Exists(recaps)) Directory.Move(recaps, backup);
            Window? window = null;
            try
            {
                var s = service.Current;
                s.MotionLevel = MotionLevel.Off;
                s.UnlockedSkills = (skills ?? Skills).ToList();
                s.CurrentSeason = s.SeasonStatsSeason = "2026-10";
                s.PlayerLevel = 1; s.SeasonPeakLevel = 0;
                s.SeasonConditioningMinutes = 0; s.SeasonPeakRank = 0; s.SeasonPeakRankTotal = 0;
                s.SeasonActiveDays = new List<string>();
                s.SeasonFeatureUse = new Dictionary<string, int>();
                var progress = new AchievementProgress();
                var tab = new EnhancementsTabView { ReadProProgress = () => progress };
                window = new Window { Width = 1400, Height = 700, Content = tab };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                test(tab, s, progress, window);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(recaps)) Directory.Delete(recaps, true);
                if (Directory.Exists(backup)) Directory.Move(backup, recaps);
                service.SaveImmediate();
                CoreSettings.ServiceProvider = old;
            }
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("ditzy_data_pro")]
    [InlineData("season_rewind")]
    [InlineData("bestie_records")]
    [InlineData("brain_drain_report")]
    public Task EachExpanderNeedsItsOwnSkillAndBuildsOnlyAtFirstOpen(string skill) => OnUi((tab, s, p, w) =>
    {
        Assert.Equal("ProToggle_" + skill, Assert.Single(All<Border>(tab).Where(b => b.Name?.StartsWith("ProToggle_") == true)).Name);
        var panel = Assert.Single(All<Border>(tab).Where(b => b.Name?.StartsWith("ProPanel_") == true));
        Assert.False(panel.IsVisible);
        Assert.Null(panel.Child);
        Assert.Same(panel, Open(tab, skill));
        Assert.True(panel.IsVisible);
        var content = panel.Child;
        var button = All<Border>(tab).Single(b => b.Name == "ProToggle_" + skill);
        Assert.Equal(IconKind.ChevronUp, All<IconGlyph>(button).Last().Kind);
        Toggle(button, Key.Space);
        Assert.False(panel.IsVisible);
        Toggle(button);
        Assert.Same(content, panel.Child);
        // No inner vertical scroll: the existing header owns the whole column's overflow.
        Assert.DoesNotContain(All<ScrollViewer>(panel), v => v.VerticalScrollBarVisibility != global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
    }, new[] { skill });

    [Fact]
    public Task HeaderOrderAndLazyFailureMatchWpf() => OnUi((tab, s, p, w) =>
    {
        Assert.Equal(Skills.Select(id => "ProToggle_" + id), All<Border>(tab).Where(b => b.Name?.StartsWith("ProToggle_") == true).Select(b => b.Name));
        int builds = 0;
        var parent = new StackPanel();
        tab.AddProSection(parent, "ditzy_data_pro", IconKind.ChartMultiple, "label_pro_lifetime_title", () => { builds++; throw new InvalidOperationException("test"); });
        Assert.Equal(0, builds);
        var button = (Border)parent.Children[0];
        var panel = (Border)parent.Children[1];
        Toggle(button);
        Assert.Equal("—", Assert.IsType<TextBlock>(panel.Child).Text);
        Toggle(button); Toggle(button);
        Assert.Equal(1, builds);
        Assert.True(panel.IsVisible);
    });

    [Fact]
    public Task LifetimeShowsAllCountersAndMondayFirstActivityWithoutInventingLegacyDays() => OnUi((tab, s, p, w) =>
    {
        s.TotalConditioningMinutes = 90;
        s.HighestLevelEver = 23; s.HighestStreak = 17; s.NightTimeUsageCount = 13; s.EarlyMorningUsageCount = 11;
        p.TotalXPEarned = 321; p.TotalSkillPointsEarned = 42; p.LifetimeSkillPointsSpent = 108;
        p.EnhancementsPlayed = 6; p.DeeperMinutes = 59; p.QuizzesPassed = 5; p.KeywordTriggersFired = 4;
        p.BlinkTrainerBlinks = 3; p.GazePops = 2; p.FastestLockCardSeconds = 1.5;
        // These arrive after construction: first expand must read the latest values and disk.
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-08", Schema = 1, DaysActive = 20 });
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-09", ActiveDays = new List<string> { "2026-09-07" } });
        var panel = Open(tab, "ditzy_data_pro");
        var cells = All<Grid>(panel).Single(g => g.ColumnDefinitions.Count == 3).Children.Cast<StackPanel>().ToArray();
        Assert.Equal(new[] { $"1.5 {Loc.Get("label_hrs")}", "321", "42", "108", "23", "17", "6", $"59 {Loc.Get("label_min_abbrev")}", "5", "4", "3", "2", "1.5s", "13", "11" }, cells.Select(c => ((TextBlock)c.Children[1]).Text));
        var days = All<Border>(panel).Where(b => b.Tag is string date && date.StartsWith("2026-09-")).ToArray();
        Assert.Equal(30, days.Length);
        Assert.DoesNotContain(All<Border>(panel), b => b.Tag is string date && date.StartsWith("2026-08-"));
        var monday = days.Single(b => Equals(b.Tag, "2026-09-07"));
        Assert.Equal(13, Canvas.GetLeft(monday)); Assert.Equal(0, Canvas.GetTop(monday));
        Assert.Equal(Colors.HotPink, ((ISolidColorBrush)monday.Background!).Color);
        Assert.Equal("2026-09-07", ToolTip.GetTip(monday));
    });

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 8)]
    public Task RewindCombinesLiveAndSnapshotsWithSchemaGatesChartsAndDeltas(bool capstone, int columns) => OnUi((tab, s, p, w) =>
    {
        if (capstone) s.UnlockedSkills.Add("certified_data_bimbo");
        s.SeasonConditioningMinutes = 120; s.SeasonPeakLevel = 12; s.PlayerLevel = 10;
        s.SeasonPeakRank = 2; s.SeasonPeakRankTotal = 10; s.SeasonPointsSpent = 7;
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-09", Schema = 2, SeasonMinutes = 60, PeakLevel = 9, PointsSpentSeason = 5 });
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-08", Schema = 1, SeasonMinutes = 90, PeakLevel = 8, PointsSpentSeason = 999 });
        var panel = Open(tab, "season_rewind");
        var table = All<Grid>(panel).Single(g => g.ColumnDefinitions.Count == columns);
        string? Cell(int row, int col) => Texts(table.Children.Single(c => Grid.GetRow(c) == row && Grid.GetColumn(c) == col)).FirstOrDefault()
            ?? (table.Children.Single(c => Grid.GetRow(c) == row && Grid.GetColumn(c) == col) as TextBlock)?.Text;
        Assert.Equal(4, table.RowDefinitions.Count);
        Assert.Equal("2026-10 •", Cell(1, 0)); Assert.Equal("2026-09", Cell(2, 0)); Assert.Equal("2026-08", Cell(3, 0));
        Assert.Equal("20%", Cell(1, 5)); Assert.Equal("12", Cell(1, 6)); Assert.Equal("—", Cell(3, 6));
        if (capstone) { Assert.Equal("7", Cell(1, 7)); Assert.Equal("5", Cell(2, 7)); Assert.Equal("—", Cell(3, 7)); }
        Assert.Contains(All<IconGlyph>(table), g => g.Kind == IconKind.ChevronUp);
        Assert.Contains(All<IconGlyph>(table), g => g.Kind == IconKind.ChevronDown);
        Assert.Equal(3, All<Border>(panel).Count(b => b.Tag is string key && key.Length == 7));
        Assert.Equal(3, All<Ellipse>(panel).Length);
        Assert.Single(All<Polyline>(panel));
        Assert.Contains(Loc.Get("label_chart_level_per_season"), Texts(panel));
    });

    [Fact]
    public Task BestieAndBrainDrainUseSnapshotRecordsAndSumEveryFeature() => OnUi((tab, s, p, w) =>
    {
        s.SeasonConditioningMinutes = 10; s.SeasonPeakRank = 9; s.SeasonPeakLevel = 3;
        s.SeasonFeatureUse[SeasonFeatureKeys.Flash] = 2;
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-09", SeasonMinutes = 120, PeakRank = 2, PeakLevel = 20,
            FeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 3, [SeasonFeatureKeys.Video] = 10 } });
        var records = Open(tab, "bestie_records");
        Assert.Contains("#2", Texts(records)); Assert.Contains("20", Texts(records)); Assert.Contains("  2026-09", Texts(records));
        Assert.Contains($"2.0 {Loc.Get("label_hrs")}", Texts(records));
        Assert.Contains("—", Texts(records)); // no fastest lock recorded
        var brain = Open(tab, "brain_drain_report");
        var rows = ((StackPanel)brain.Child!).Children.Skip(1).Cast<Grid>().ToArray();
        Assert.Equal(SeasonFeatureKeys.Catalog.Count, rows.Length);
        Assert.Equal(Loc.Get("recap_feat_video"), Texts(rows[0])[0]);
        Assert.Contains("0 / 10", Texts(rows[0])); Assert.Contains("2 / 5", Texts(rows[1]));
        var bars = rows.Select(r => All<Border>(r).Single()).ToArray();
        Assert.Equal(168, bars[0].Width); Assert.Equal(88, bars[1].Width); Assert.Equal(8, bars.Last().Width);
        Assert.Equal(0.25, bars.Last().Opacity);
    });

    [Fact]
    public Task RewindCapsHistoryAndDoesNotDuplicateItsLiveBucket() => OnUi((tab, s, p, w) =>
    {
        for (int i = 0; i < 13; i++)
        {
            var key = new DateTime(2025, 9, 1).AddMonths(i).ToString("yyyy-MM");
            SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = key, SeasonMinutes = i + 1, PeakLevel = i + 1 });
        }
        // A desynced live-key snapshot must not create a second live row.
        SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-10", SeasonMinutes = 999 });
        var panel = Open(tab, "season_rewind");
        var table = All<Grid>(panel).Single(g => g.ColumnDefinitions.Count == 7);
        Assert.Equal(9, table.RowDefinitions.Count); // live + seven older: Take(8) includes the skipped live key
        Assert.Single(table.Children.OfType<TextBlock>(), t => t.Text == "2026-10 •");
        Assert.Equal(10, All<Border>(panel).Count(b => b.Tag is string key && key.Length == 7));
        Assert.DoesNotContain("2025-09", Texts(panel));
    });

    [Fact]
    public Task EmptyPanelsKeepWpfDashesAndRewindHint() => OnUi((tab, s, p, w) =>
    {
        s.PlayerLevel = s.SeasonPeakLevel = 0;
        var rewind = Open(tab, "season_rewind");
        Assert.Contains(Loc.Get("label_rewind_empty"), Texts(rewind));
        Assert.Empty(All<Ellipse>(rewind));
        var lifetime = Open(tab, "ditzy_data_pro");
        Assert.Contains("—", Texts(lifetime)); // no heatmap, unknown fastest lock
        tab.ReadProProgress = () => null;
        Assert.Empty(((StackPanel)Open(tab, "bestie_records").Child!).Children);
    });
}
