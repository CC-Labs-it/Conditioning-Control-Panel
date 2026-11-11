using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Platform;
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
            App.StopBrainDrainQuestMinutes();
            CoreEngine.StartBrainDrain = oldStart;
            CoreEngine.StopBrainDrain = oldStop;
            CoreSettings.ServiceProvider = oldSettings;
            App.Quests = oldQuests;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void PresetEnablingBrainDrainCreditsOnlyAVisualRun(bool audioOnly, int expected) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = App.Quests;
        var oldStart = CoreEngine.StartBrainDrain;
        var oldStop = CoreEngine.StopBrainDrain;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var oldPlayer = BrainDrainPlayer.Instance;
        var dir = Directory.CreateTempSubdirectory("ccp-brain-preset-").FullName;
        using var quests = new QuestService(null, dir);
        var quest = new ActiveQuest("soft_static_d");
        quests.Progress.DailyQuests = new List<ActiveQuest> { quest };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSession.IsSessionRunningProvider = () => false;
        App.Quests = quests;
        BrainDrainPlayer.Instance = null; // No LibVLC player is available.
        try
        {
            var s = settings.Current;
            s.BrainDrainEnabled = false;
            s.AudioOnlySession = audioOnly;
            s.FlashEnabled = false;
            var preset = Preset.FromSettings(s, "Brain Drain");
            preset.BrainDrainEnabled = true;
            App.WireBrainDrainQuestMinutes();
            var timer = (global::Avalonia.Threading.DispatcherTimer)typeof(App)
                .GetField("_brainDrainQuestTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            Assert.True(timer.IsEnabled);
            CoreEngine.Start();
            CoreEngine.Reconcile(); // A disabled preset stops audio, but must not stop sampling.
            Assert.True(timer.IsEnabled);
            App.SampleBrainDrainQuestMinutes(TimeSpan.Zero);
            Assert.True(new PresetsTabView().LoadPreset(preset));
            Assert.True(s.BrainDrainEnabled);
            // Audio-only is latched at engine start, even if the preference changes mid-run.
            s.AudioOnlySession = !audioOnly;
            for (int second = 1; second <= 121; second++)
                App.SampleBrainDrainQuestMinutes(TimeSpan.FromSeconds(second));
            Assert.Equal(expected, quest.CurrentProgress);
            CoreEngine.Stop();
            for (int second = 122; second <= 242; second++)
                App.SampleBrainDrainQuestMinutes(TimeSpan.FromSeconds(second));
            Assert.Equal(expected, quest.CurrentProgress);
        }
        finally
        {
            CoreEngine.Stop();
            App.StopBrainDrainQuestMinutes();
            CoreEngine.StartBrainDrain = oldStart;
            CoreEngine.StopBrainDrain = oldStop;
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreSettings.ServiceProvider = oldSettings;
            App.Quests = oldQuests;
            BrainDrainPlayer.Instance = oldPlayer;
            try { Directory.Delete(dir, true); } catch { }
        }
    });

}
