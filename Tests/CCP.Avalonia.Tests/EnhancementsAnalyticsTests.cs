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
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using IconGlyph = ConditioningControlPanel.Avalonia.Controls.IconGlyph;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class EnhancementsAnalyticsTests
{
    [Fact]
    public Task OwnedProPanelsBuildLazilyInsideHeaderAndKeyboardTogglesReuseCharts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldEngine = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("ccp-pro-").FullName;
        Window? w = null;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            property.SetValue(null, engine);
            var s = service.Current;
            s.MotionLevel = MotionLevel.Off;
            s.UnlockedSkills = new List<string>();
            var tab = new EnhancementsTabView();
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(tab.GetLogicalDescendants().OfType<Border>(), b => b.Name?.StartsWith("ProToggle_") == true);
            s.SeasonStatsSeason = "2026-10";
            s.SeasonConditioningMinutes = 125;
            s.SeasonActiveDays = new List<string> { "2026-10-01", "2026-10-02" };
            s.PlayerLevel = 23;
            s.SeasonFeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 7 };
            s.UnlockedSkills = new List<string> { "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo" };
            Dispatcher.UIThread.RunJobs();
            s.TotalConditioningMinutes = 600; // after repaint: content must read this only on first expand
            Border Named(string name) => tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == name);
            string[] Texts(Control panel) => panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            foreach (var skill in s.UnlockedSkills.Take(4))
            {
                var toggle = Named("ProToggle_" + skill);
                var panel = Named("ProPanel_" + skill);
                Assert.Null(panel.Child);
                Assert.False(panel.IsVisible);
                toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.True(panel.IsVisible);
                Assert.NotNull(panel.Child);
                var built = panel.Child;
                toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
                Assert.False(panel.IsVisible);
                toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Same(built, panel.Child);
            }
            Assert.Contains(EnhancementAnalytics.FormatMinutes(600), Texts(Named("ProPanel_ditzy_data_pro")));
            Assert.Contains(Loc.Get("label_heatmap_title"), Texts(Named("ProPanel_ditzy_data_pro")));
            Assert.Contains(Loc.Get("label_chart_time_per_season"), Texts(Named("ProPanel_season_rewind")));
            Assert.Contains(Loc.Get("label_rewind_spent"), Texts(Named("ProPanel_season_rewind")));
            Assert.Contains("23", Texts(Named("ProPanel_bestie_records")));
            Assert.Contains("7 / 7", Texts(Named("ProPanel_brain_drain_report")));
            Assert.Contains(Named("ProPanel_ditzy_data_pro").GetLogicalDescendants().OfType<Border>(),
                b => ToolTip.GetTip(b) as string == "2026-10-01");
            Assert.Contains(tab.GetLogicalDescendants().OfType<ScrollViewer>(), sv => sv.MaxHeight == 430);
            if (Environment.GetEnvironmentVariable("CCP_ENHANCEMENT_SCREENSHOT") is { } png)
            { Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame()?.Save(png); }
            s.UnlockedSkills = s.UnlockedSkills.Where(id => id != "certified_data_bimbo").ToList();
            Dispatcher.UIThread.RunJobs();
            Named("ProToggle_season_rewind").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.DoesNotContain(Loc.Get("label_rewind_spent"), Texts(Named("ProPanel_season_rewind")));
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = provider;
            property.SetValue(null, oldEngine);
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SeasonRewindTimeDeltasAreFluentArrowsNotTextTriangles() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var recaps = Path.Combine(CorePaths.UserData, "season-recaps");
        Window? w = null;
        try
        {
            if (Directory.Exists(recaps)) Directory.Delete(recaps, true);
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.MotionLevel = MotionLevel.Off;
            s.SeasonStatsSeason = "2026-10";
            s.SeasonConditioningMinutes = 125;                 // live 125 > 50: up
            SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-09", SeasonMinutes = 50 });  // 50 < 80: down
            SeasonRecapStore.Save(new SeasonRecapSnapshot { SeasonKey = "2026-08", SeasonMinutes = 80 });
            s.UnlockedSkills = new List<string> { "season_rewind" };
            var tab = new EnhancementsTabView();
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ProToggle_season_rewind");
            toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            var panel = tab.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ProPanel_season_rewind");
            var kinds = panel.GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind).ToList();
            Assert.Equal(1, kinds.Count(k => k == IconKind.ArrowUp));
            Assert.Equal(1, kinds.Count(k => k == IconKind.ArrowDown));
            Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains('\u25B2') || (t.Text ?? "").Contains('\u25BC'));
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = provider;
            if (Directory.Exists(recaps)) Directory.Delete(recaps, true);
        }
        return Task.CompletedTask;
    });
}
