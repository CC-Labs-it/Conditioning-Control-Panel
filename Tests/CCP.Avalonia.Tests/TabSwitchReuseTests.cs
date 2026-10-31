using System;
using System.Linq;
using Avalonia.Media;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.UI;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabSwitchReuseTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task NavigationRecordsRecentWithoutRebuildingHiddenHome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            service.Current.HasSeenProgramsIntro = true;
            service.Current.WhatMovedCardShown = 1;   // P76
            service.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            service.Current.RailRecent.Add("tab.quests");
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("settings");
            var home = shell.Named<SettingsTabView>("SettingsTab")!;
            var recent = home.FindControl<StackPanel>("RecentList")!;
            var painted = recent.Children.ToArray();
            Assert.NotEmpty(painted);
            shell.ShowTab("programs");
            shell.ShowTab("blinktrainer");
            Assert.Equal(painted, recent.Children.ToArray());
            Assert.Contains("blinktrainer", service.Current.RailRecent.First());
            shell.ShowTab("settings");
            Assert.NotEqual(painted, recent.Children.ToArray());
            Assert.Contains(recent.Children.OfType<Button>(), b => (string?)b.Tag == service.Current.RailRecent.First());
        }
        finally { shell?.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EnhancementsKeepsUnchangedNodesAndRefreshesLiveHeaderAndChangedSkills() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? window = null;
        try
        {
            var s = service.Current;
            s.MotionLevel = MotionLevel.Off;
            var tab = new EnhancementsTabView();
            window = new Window { Content = tab };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var canvas = tab.FindControl<Canvas>("SkillTreeCanvas")!;
            var nodes = canvas.Children.Where(c => c.Tag is string).ToArray();
            Assert.NotEmpty(nodes);
            tab.IsVisible = false;
            s.TotalConditioningMinutes = 125;
            tab.IsVisible = true;
            Assert.Equal(nodes, canvas.Children.Where(c => c.Tag is string).ToArray());
            Assert.Equal(SkillTreeRules.FormatConditioningTime(s), tab.FindControl<TextBlock>("TxtConditioningTime")!.Text);
            // The visible canvas header (not just the hidden field) shows the new time.
            Assert.Contains(canvas.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == SkillTreeRules.FormatConditioningTime(s));
            // A mod change while hidden invalidates the retained nodes (names and art are mod-aware).
            tab.IsVisible = false;
            CoreMods.RaiseModChanged(null, new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true));
            Dispatcher.UIThread.RunJobs();
            tab.IsVisible = true;
            var afterMod = canvas.Children.Where(c => c.Tag is string).ToArray();
            Assert.DoesNotContain(afterMod, nodes.Contains);
            nodes = afterMod;
            tab.IsVisible = false;
            s.UnlockedSkills.Add("pink_hours"); // in-place list mutation raises no PropertyChanged
            tab.IsVisible = true;
            Assert.NotEqual(nodes, canvas.Children.Where(c => c.Tag is string).ToArray());
            Assert.NotEmpty(tab.OwnedGlows);
        }
        finally { window?.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task SectionPaletteChangesOnceWithAllColorsReady() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var app = Application.Current!;
        var changes = 0;
        EventHandler<ResourcesChangedEventArgs> changed = (_, _) => changes++;
        app.ResourcesChanged += changed;
        try
        {
            MainShellWindow.PaintSectionInk(NavSections.Play);
            Assert.Equal(1, changes);
            foreach (var key in new[] { "SectionInk", "SectionTint", "SectionRule", "SectionOutline" })
            {
                Assert.True(app.TryFindResource(key, out var color));
                Assert.True(app.TryFindResource(key + "Brush", out var brush));
                Assert.Equal((Color)color!, Assert.IsType<SolidColorBrush>(brush).Color);
            }
            Assert.True(app.TryFindResource("SectionInk", out var ink));
            Assert.Equal(NavStripRules.Ink(NavSections.Play), (Color)ink!);
        }
        finally
        {
            app.ResourcesChanged -= changed;
            MainShellWindow.PaintSectionInk(NavSections.Settings);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BlinkTrainerRetainsLocalizedActionControlsOnRefresh() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tab = new BlinkTrainerTabView();
        tab.Refresh();
        var consent = tab.FindControl<Button>("BtnBlinkTrainerManageConsent")!;
        var action = tab.FindControl<Button>("BlinkTrainerStatusAction")!;
        var consentLabel = consent.Content;
        var actionLabel = action.Content;
        tab.Refresh();
        Assert.Same(consentLabel, consent.Content);
        Assert.Same(actionLabel, action.Content);
        return Task.CompletedTask;
    });

}
