using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed class CompanionCommunityPromptsTests
{
    [Fact]
    public Task RowsActivateRemoveKeepEmptyPlaceholderAndRepaintAssignment() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var oldIds = s.InstalledCommunityPromptIds;
        var oldPrompt = s.CompanionPrompt;
        var oldActive = s.ActiveCommunityPromptId;
        var oldAssignment = s.GetCompanionPromptId(0);
        var oldCompanion = s.ActiveCompanionId;
        var oldSlut = s.SlutModeEnabled;
        var source = Path.Combine(CorePaths.UserData, "community-test.json");
        File.WriteAllText(source, JsonConvert.SerializeObject(new CommunityPrompt { Author = "Anonymous", PromptSettings = new() { Personality = "Friendly" } }));
        s.InstalledCommunityPromptIds = new(); s.ActiveCommunityPromptId = null; s.SlutModeEnabled = false; s.ActiveCompanionId = 0;
        var shell = new MainShellWindow();
        var room = new CompanionRoomView();
        var community = room.WorkshopParts!.Community;
        var panel = community.FindControl<StackPanel>("InstalledPromptsPanel")!;
        var placeholder = community.FindControl<TextBlock>("TxtNoInstalledPrompts")!;
        var prompt = CommunityPromptsCore.Shared.ImportFromFile(source)!;
        try
        {
            shell.RefreshCommunityPrompts(room);
            Assert.Contains(placeholder, panel.Children);
            Assert.False(placeholder.IsVisible);
            var activate = panel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, prompt.Id) && b.Content?.ToString() != "×");
            activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(prompt.Id, s.ActiveCommunityPromptId);
            Assert.Single(panel.GetLogicalDescendants().OfType<Button>());
            shell.AssignImportedPersonality(0, prompt.Id);
            s.CompanionPrompt.Personality = "Edited";
            shell.AssignImportedPersonality(0, prompt.Id);
            Assert.Equal("Friendly", s.CompanionPrompt.Personality);
            shell.RefreshCommunityPrompts(room);
            Assert.Equal(prompt.Name, room.WorkshopParts.Roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            panel.GetLogicalDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.True(placeholder.IsVisible);
            Assert.Single(panel.Children);
            shell.RefreshCommunityPrompts(room);
            Assert.Same(placeholder, panel.Children.Single());
        }
        finally
        {
            CommunityPromptsCore.Shared.RemovePrompt(prompt.Id);
            s.InstalledCommunityPromptIds = oldIds; s.CompanionPrompt = oldPrompt; s.ActiveCommunityPromptId = oldActive;
            s.SetCompanionPromptId(0, oldAssignment); s.ActiveCompanionId = oldCompanion; s.SlutModeEnabled = oldSlut;
            File.Delete(source); shell.Close();
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task CommunityDialogsAreClosedByRegisteredPanicSurface() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Boot();
        var shell = new MainShellWindow();
        var dialog = new MessageDialog("Community", "Install first prompt?", true);
        var result = shell.ShowCommunityDialogAsync(dialog);
        Assert.True(dialog.IsVisible);
        PanicSurfaces.All.Single(s => s.Id == "community-prompts").Stop(shell);
        Assert.False(await result);
        Assert.False(dialog.IsVisible);
        MainShellWindow.StopCommunityPrompts();
        shell.Close();
    });
    [Fact]
    public Task TabSyncReadsTriggerStateWithoutWritingIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var s = CoreSettings.Current;
        var old = (s.TriggerModeEnabled, s.TriggerIntervalSeconds);
        try
        {
            var cell = new WorkshopTriggersCell();
            s.TriggerModeEnabled = true; s.TriggerIntervalSeconds = 90;
            cell.SyncFromSettings();
            Assert.True(cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked);
            Assert.True(cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
            Assert.Equal("90s", cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text);
            s.TriggerModeEnabled = false; cell.SyncFromSettings();
            Assert.False(cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
            Assert.Equal(90, s.TriggerIntervalSeconds);
        }
        finally { (s.TriggerModeEnabled, s.TriggerIntervalSeconds) = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task BrowsePreservesServerOrderAndLimitsPreviewToFive() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Boot();
        var prompts = Enumerable.Range(1, 6).Select(i => new CommunityPromptManifestEntry
            { Id = i.ToString(), Name = "Prompt " + i, Author = "Anonymous", Description = "Description " + i }).ToList();
        var message = MainShellWindow.CommunityBrowseMessage(prompts);
        Assert.Contains("Prompt 1 by Anonymous", message);
        Assert.Contains("Prompt 5 by Anonymous", message);
        Assert.DoesNotContain("Prompt 6", message);
        Assert.True(message.IndexOf("Prompt 1", StringComparison.Ordinal) < message.IndexOf("Prompt 5", StringComparison.Ordinal));
        Assert.EndsWith(Loc.GetF("label_and_more_prompts", 1) + Loc.Get("label_install_first_one"), message);
        return Task.CompletedTask;
    });
    private static void Boot()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
