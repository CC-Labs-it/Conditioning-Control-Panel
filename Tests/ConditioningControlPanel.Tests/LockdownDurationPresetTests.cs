using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Lockdown length picker (LockdownTabView.xaml, CmbLockdownDuration). Staff report 2026-11-18:
/// the 40 minute "Surrender" quest had no matching lockdown, the list jumped from 30 to 60.
/// 40 and 50 sit between 30 and 60; the default (20 minutes, SelectedIndex 3) does not move.
/// </summary>
public class LockdownDurationPresetTests
{
    private static readonly int[] Expected = { 5, 10, 15, 20, 30, 40, 50, 60, 120, 240 };

    [Fact]
    public void The_picker_offers_forty_and_fifty_in_order()
    {
        var (tags, keys, _) = Picker();
        Assert.Equal(Expected, tags);
        Assert.Equal("btn_40_minutes", keys[tags.IndexOf(40)]);
        Assert.Equal("btn_50_minutes", keys[tags.IndexOf(50)]);
    }

    [Fact]
    public void The_default_is_still_twenty_minutes()
    {
        var (tags, _, index) = Picker();
        Assert.Equal(3, index);
        Assert.Equal(20, tags[index]);
    }

    [Fact]
    public void Every_length_has_a_label_in_every_language()
    {
        var (tags, keys, _) = Picker();
        var langs = Directory.GetFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages"), "*.json");
        Assert.Equal(10, langs.Length);
        foreach (var file in langs)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            for (int i = 0; i < keys.Count; i++)
            {
                Assert.True(doc.RootElement.TryGetProperty(keys[i], out var v), $"{Path.GetFileName(file)} has no {keys[i]}");
                // The hour keys say "2"/"4", the minute keys say the minutes.
                var shown = tags[i] >= 120 ? (tags[i] / 60).ToString() : tags[i].ToString();
                Assert.Contains(shown, v.GetString());
            }
        }
    }

    /// <summary>
    /// Every single-session timed target a player is asked for (daily Takeover quests, program
    /// Takeover / Surrender Control tasks, program "Lockdown of N minutes" tasks) has a lockdown
    /// length that covers it without overshooting by more than five minutes. Weekly totals are
    /// spread over many sessions and are left out.
    /// </summary>
    [Fact]
    public void Every_timed_quest_and_program_task_has_a_close_lockdown_length()
    {
        var (tags, _, _) = Picker();
        var targets = TimedTargets();
        Assert.Contains(targets, t => t.Minutes == 40); // the Surrender quest that started this
        foreach (var (what, minutes) in targets)
        {
            var fit = tags.Where(t => t >= minutes).DefaultIfEmpty(-1).Min();
            Assert.True(fit >= minutes && fit - minutes <= 5,
                $"{what} needs {minutes} min; the closest lockdown is {(fit < 0 ? "none" : fit + " min")}");
        }
    }

    internal static List<(string What, int Minutes)> TimedTargets()
    {
        var list = new List<(string, int)>();
        foreach (var q in QuestDefinition.DailyQuests.Where(q => q.Category == QuestCategory.Autonomy))
            list.Add(($"daily quest {q.Id}", q.TargetValue));

        var lockdownMinutes = new Regex(@"Lockdown of (\d+) minutes");
        foreach (var p in BuiltInPrograms.All())
            foreach (var d in p.Chapters.SelectMany(c => c.Days))
                foreach (var t in d.Tasks)
                {
                    if (t.Verifier == QuestCategory.Autonomy)
                        list.Add(($"{p.Id} {t.Id}", t.TargetValue));
                    else if (t.Verifier == QuestCategory.Lockdown && lockdownMinutes.Match(t.Description) is { Success: true } m)
                        list.Add(($"{p.Id} {t.Id}", int.Parse(m.Groups[1].Value)));
                }
        list.Add(("lockdown quest minimum", (int)QuestService.LockdownQuestMinimum.TotalMinutes));
        return list;
    }

    private static (List<int> Tags, List<string> Keys, int SelectedIndex) Picker()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "LockdownTabView.xaml"));
        var combo = Regex.Match(xaml, "<ComboBox[^>]*x:Name=\"CmbLockdownDuration\"[\\s\\S]*?</ComboBox>").Value;
        Assert.NotEmpty(combo);
        var index = int.Parse(Regex.Match(combo, "SelectedIndex=\"(\\d+)\"").Groups[1].Value);
        var items = Regex.Matches(combo, "<ComboBoxItem Content=\"\\{loc:Str (\\w+)\\}\" Tag=\"(\\d+)\"/>");
        var keys = items.Select(m => m.Groups[1].Value).ToList();
        var tags = items.Select(m => int.Parse(m.Groups[2].Value)).ToList();
        Assert.Equal(Regex.Matches(combo, "<ComboBoxItem").Count, tags.Count);
        return (tags, keys, index);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
