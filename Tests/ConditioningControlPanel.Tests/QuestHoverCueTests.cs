using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Staff ask, 2026-11-18: a quest card that opens its feature says so. Hand cursor and a light
/// highlight on hover, a small go hint on the art, a soft click on press. Quest logic and rewards
/// are untouched; these pin the cue, not the trip (QuestDestinationsTests owns the trip).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class QuestHoverCueTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void The_click_follows_the_master_volume_and_stays_quiet()
    {
        Assert.Equal(0f, QuestSfx.Level(0, sessionRunning: false));
        Assert.Equal(0f, QuestSfx.Level(-5, sessionRunning: false));
        Assert.Equal(QuestSfx.OpenScale, QuestSfx.Level(100, sessionRunning: false), 4);
        Assert.Equal(QuestSfx.OpenScale * 0.5f, QuestSfx.Level(50, sessionRunning: false), 4);
        Assert.Equal(QuestSfx.OpenScale * 0.5f, QuestSfx.Level(100, sessionRunning: true), 4);
        Assert.Equal(QuestSfx.OpenScale, QuestSfx.Level(250, sessionRunning: false), 4);
        // Quieter than the launcher's own click (0.16) and the friends drawer's (0.12).
        Assert.True(QuestSfx.OpenScale <= 0.12f);
    }

    [Fact]
    public void The_click_reuses_a_shipped_ui_sound()
    {
        var file = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds",
            QuestSfx.OpenCue.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"{QuestSfx.OpenCue} is not in Resources/sounds");
    }

    [Fact]
    public void Both_doors_click_before_they_navigate()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "QuestsTabView.xaml.cs"));
        Assert.Matches(new Regex(@"QuestSfx\.Open\(\);\s*mw\.OpenDailyQuestDestination\("), src);
        Assert.Matches(new Regex(@"QuestSfx\.Open\(\);\s*mw\.OpenWeeklyQuestDestination\(\)"), src);
    }

    [Fact]
    public void The_go_hint_is_hidden_at_rest_and_never_takes_the_click() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = new DailyQuestCard();
        Assert.Equal(Visibility.Collapsed, card.GoHint.Visibility);
        Assert.False(card.GoHint.IsHitTestVisible);

        var tab = new QuestsTabView();
        Assert.Equal(Visibility.Collapsed, tab.WeeklyGoHint.Visibility);
        Assert.False(tab.WeeklyGoHint.IsHitTestVisible);
        Assert.Equal(0, tab.WeeklyHoverRim.Opacity);
        Assert.False(tab.WeeklyHoverRim.IsHitTestVisible);
    });

    [Fact]
    public void The_go_hint_comes_up_on_hover() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = new DailyQuestCard();
        QuestGoHint.Set(card.GoHint, true);
        Assert.Equal(Visibility.Visible, card.GoHint.Visibility);

        var tab = new QuestsTabView();
        tab.SetWeeklyNavigable(true);
        tab.SetWeeklyHover(true);
        Assert.Equal(Visibility.Visible, tab.WeeklyGoHint.Visibility);
    });

    [Fact]
    public void A_plain_weekly_card_shows_no_hand() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();
        tab.SetWeeklyNavigable(true);
        Assert.Same(System.Windows.Input.Cursors.Hand, tab.WeeklyQuestCard.Cursor);
        tab.SetWeeklyNavigable(false);
        Assert.Null(tab.WeeklyQuestCard.Cursor);
    });
}
