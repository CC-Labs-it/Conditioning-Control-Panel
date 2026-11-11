using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class BrainDrainQuestMinutesTests
{
    [Fact]
    public void RunningCreditStopsWithEngineAndDisabledSetting() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = App.Quests;
        var oldStart = CoreEngine.StartBrainDrain;
        var oldStop = CoreEngine.StopBrainDrain;
        var dir = Directory.CreateTempSubdirectory("ccp-brain-quest-").FullName;
        using var quests = new QuestService(null, dir);
        var quest = new ActiveQuest("soft_static_d");
        quests.Progress.DailyQuests = new List<ActiveQuest> { quest };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        App.Quests = quests;
        try
        {
            settings.Current.BrainDrainEnabled = true;
            App.WireBrainDrainQuestMinutes();
            CoreEngine.Start();
            App.SampleBrainDrainQuestMinutes(TimeSpan.Zero);
            for (int second = 1; second <= 120; second++)
                App.SampleBrainDrainQuestMinutes(TimeSpan.FromSeconds(second));
            Assert.Equal(2, quest.CurrentProgress);
            settings.Current.BrainDrainEnabled = false;
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(3));
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(4));
            Assert.Equal(2, quest.CurrentProgress);
            CoreEngine.Stop();
            settings.Current.BrainDrainEnabled = true;
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(5));
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(6));
            Assert.Equal(2, quest.CurrentProgress);
            CoreEngine.Start();
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(7));
            RemoteCommands.StopEffects(force: true);
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(8));
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromMinutes(9));
            Assert.Equal(2, quest.CurrentProgress);
        }
        finally
        {
            CoreEngine.Stop();
            CoreEngine.StartBrainDrain = oldStart;
            CoreEngine.StopBrainDrain = oldStop;
            CoreSettings.ServiceProvider = oldSettings;
            App.Quests = oldQuests;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
