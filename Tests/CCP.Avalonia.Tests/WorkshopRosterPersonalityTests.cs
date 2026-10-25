using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopRosterPersonalityTests
{
    [Fact]
    public Task PersonalityButtonImportsWithoutSwitchingAndRefreshesLabels() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var folder = Path.Combine(Path.GetTempPath(), "ccp-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "Calm persona.json");
        File.WriteAllText(file, "{\"Name\":\"ignored embedded name\",\"PromptSettings\":{\"Personality\":\"calm\"}}");
        var drawer = new WorkshopAccordion();
        var owner = new Window { Content = drawer, Width = 700, Height = 800 };
        try
        {
            CoreSettings.Current.ActiveCompanionId = 0;
            CoreSettings.Current.SlutModeEnabled = false;
            drawer.ViewModel!.IsExpanded = true;
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            var roster = ((WorkshopRuntimeVm)drawer.DataContext!).Parts.Roster;
            int picks = 0;
            drawer.RosterPromptPicker = (_, name) => { picks++; return Task.FromResult<string?>(file); };
            roster.FindControl<Button>("BtnCompanion0Personality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, picks);
            Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
            Assert.Equal("calm", CoreSettings.Current.CompanionPrompt.Personality);
            Assert.True(CoreSettings.Current.CompanionPrompt.UseCustomPrompt);
            Assert.Equal("Calm persona", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            Assert.NotNull(ToolTip.GetTip(roster.FindControl<TextBlock>("TxtCompanion0Prompt")!));
            Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(x => x.Id == "roster-personality").Stop(null);
            Dispatcher.UIThread.RunJobs();
            var id = CoreSettings.Current.GetCompanionPromptId(0);
            drawer.RosterPromptPicker = (_, _) => Task.FromResult<string?>(null);
            await drawer.AssignRosterPersonalityAsync(roster, 0);
            Assert.Equal(id, CoreSettings.Current.GetCompanionPromptId(0));
            Assert.Empty(owner.OwnedWindows);
            CoreSettings.Current.SetCompanionPromptId(0, "missing");
            roster.Refresh();
            Assert.Equal("", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            Assert.Null(ToolTip.GetTip(roster.FindControl<TextBlock>("TxtCompanion0Prompt")!));
        }
        finally
        {
            owner.Close();
            foreach (var id in service.Current.InstalledCommunityPromptIds)
                File.Delete(Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName, id + ".json"));
            Directory.Delete(folder, true);
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = old;
        }
    });
}
