using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class QuestPresentationRefreshTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LivePresentationRepaintsAndDetachReleasesTheObserver(bool modeChange) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-quest-presentation-").FullName;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        using var definitions = new QuestDefinitionService(dir);
        using var quests = new QuestService(null, dir);
        AvApp.QuestDefinitions = definitions;
        AvApp.Quests = quests;
        var tab = new QuestsTabView();
        var host = new Window { Content = tab };
        // Raise the same event as a catalog response, without a network request.
        var updated = typeof(QuestDefinitionService).GetField("QuestDefinitionsUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Change()
        {
            if (modeChange) settings.Current.ActiveModId = settings.Current.IsSissyMode ? BuiltInMods.BambiSleepId : BuiltInMods.SissyHypnoId;
            else ((Action?)updated.GetValue(definitions))?.Invoke();
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var title = tab.FindControl<TextBlock>("TxtSeasonTitle")!;
            var def = QuestDefinition.DailyQuests[0];
            var art = QuestsTabView.GetQuestArt(def);
            Assert.NotNull(art);
            title.Text = "stale";
            Change();
            Assert.Equal(definitions.SeasonTitle, title.Text);
            if (modeChange) Assert.NotSame(art, QuestsTabView.GetQuestArt(def));
            host.Content = null;
            title.Text = "detached";
            Change();
            Assert.Equal("detached", title.Text);
            Assert.Null(updated.GetValue(definitions));
        }
        finally
        {
            host.Close();
            AvApp.Quests = null;
            AvApp.QuestDefinitions = null;
            Directory.Delete(dir, true);
        }
    });
}
