using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class QuestsModRepaintTests
{
    [Fact]
    public void VisibleRepaintsOnModAndLanguageAndHiddenRepaintsOnShow() => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Directory.CreateTempSubdirectory("quests-repaint-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldQuests = AvApp.Quests;
        var oldService = CoreMods.Service;
        var snapshot = new CoreModsSnapshot();
        var settings = new SettingsService();
        settings.RestoreFrom(new AppSettings());
        CoreSettings.ServiceProvider = () => settings;
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        using var quests = new QuestService(null, dir);
        var mods = new ModService();
        CoreMods.Attach(mods);
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("bimbo_basics_d") };
        quests.Progress.WeeklyQuest = new ActiveQuest("bubble_storm_w");
        AvApp.Quests = quests;
        var tab = new QuestsTabView();
        var host = new Window { Content = tab };
        try
        {
            loc.SetLanguage("en");
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var weekly = tab.FindControl<TextBlock>("TxtWeeklyQuestName")!;
            var daily = tab.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<TextBlock>("TxtName")!;
            var def = QuestDefinition.DailyQuests.Find(d => d.Id == "bimbo_basics_d")!;
            var before = daily.Text;
            mods.ActivateMod(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, daily.Text);
            Assert.Equal(mods.MakeModAware("Bimbo Basics"), daily.Text);
            loc.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(mods.MakeModAware(def.LocalizedName), daily.Text);
            Assert.NotEqual(mods.MakeModAware("Bimbo Basics"), daily.Text);
            tab.IsVisible = false;
            daily.Text = "stale";
            weekly.Text = "stale";
            loc.SetLanguage("en");
            mods.ActivateMod(BuiltInMods.CCPDefaultId);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("stale", daily.Text);
            Assert.Equal("stale", weekly.Text);
            tab.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(mods.MakeModAware(def.LocalizedName), daily.Text);
            Assert.Equal("Bubble Storm", weekly.Text);
        }
        finally
        {
            host.Close();
            loc.SetLanguage(language);
            settings.SaveImmediate();
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldProvider;
            AvApp.Quests = oldQuests;
            snapshot.Dispose();
            typeof(CoreMods).GetProperty(nameof(CoreMods.Service))!.SetValue(null, oldService);
            Directory.Delete(dir, true);
        }
    });
}
