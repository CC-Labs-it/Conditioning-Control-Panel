using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionRosterSwitchTests
{
    [Fact]
    public Task RealRosterCardsSwitchCoreAndRefreshHeroAndActiveRing() => WithRoster((shell, room, roster) =>
    {
        int switches = 0;
        void Switched(CompanionId _) => switches++;
        CompanionCore.Switched += Switched;
        CoreSettings.Current.AvatarEnabled = true;   // fixture parks her off; this fact needs the tube
        shell.ShowAvatarTube();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(shell.Tube);
        try
        {
            foreach (var (index, set) in new[] { (2, 5), (4, 7) })
            {
                var card = roster.FindControl<Border>($"CompanionCard{index}")!;
                Click(shell, card, MouseButton.Left);
                Assert.Equal(index, CoreSettings.Current.ActiveCompanionId);
                Assert.Equal((CompanionId)index, CompanionCore.ActiveCompanion);
                var hero = room.GetVisualDescendants().OfType<CompanionHeroCard>().Single().ViewModel!;
                Assert.Contains(CompanionDefinition.GetById(index).GetDisplayName(false), hero.Name);
                Assert.Equal(index + 2, hero.Level);
                Assert.NotEqual(Colors.Transparent, ((ISolidColorBrush)card.BorderBrush!).Color);
                Assert.Equal(Colors.Transparent, ((ISolidColorBrush)roster.FindControl<Border>("CompanionCard0")!.BorderBrush!).Color);
                Assert.NotEqual(DateTime.MinValue, CoreSettings.Current.ActiveCompanionProgress.FirstActivated);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(set, shell.Tube!.CurrentAvatarSet); // WPF SwitchToCompanionAvatar
                Assert.Equal(set, CoreSettings.Current.SelectedAvatarSet);
            }
            Assert.Equal(2, switches);
            Click(shell, roster.FindControl<Border>("CompanionCard4")!, MouseButton.Left);
            Assert.Equal(2, switches); // selecting the active card is the Core no-op path
        }
        finally { CompanionCore.Switched -= Switched; }
    });

    [Fact]
    public Task RightClickAndPersonalityButtonDoNotSwitchTheCard() => WithRoster((shell, room, roster) =>
    {
        int personalityRequests = 0;
        shell.PersonalityFileForTests = _ => Task.FromResult<string?>(null); // picker cancelled
        roster.PersonalityAssignRequested += (_, index) => { Assert.Equal(2, index); personalityRequests++; };
        Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Right);
        Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
        Click(shell, roster.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
        Assert.Equal(1, personalityRequests);
        Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
    });

    [Fact]
    public Task PersonalityButtonImportsAssignsLabelsAndActivatesOnSwitch() => WithRoster((shell, room, roster) =>
    {
        var s = CoreSettings.Current;
        var oldAssignments = s.CompanionPromptAssignments;
        var oldInstalled = s.InstalledCommunityPromptIds;
        var oldActivePrompt = s.ActiveCommunityPromptId;
        var oldPrompt = s.CompanionPrompt;
        s.CompanionPromptAssignments = new();
        s.InstalledCommunityPromptIds = new();
        s.ActiveCommunityPromptId = null;
        var dir = Directory.CreateTempSubdirectory("ccp-persona-").FullName;
        string Fixture(string name)
        {
            var path = Path.Combine(dir, name + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new CommunityPrompt
            { Id = "ignored", Name = "ignored", Author = "Test", PromptSettings = new() { Personality = "fixture " + name } }));
            return path;
        }
        try
        {
            // Inactive companion 2: assigned and labelled, but not activated (WPF :436).
            shell.PersonalityFileForTests = _ => Task.FromResult<string?>(Fixture("Second Persona"));
            Click(shell, roster.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
            var second = s.GetCompanionPromptId(2);
            Assert.False(string.IsNullOrEmpty(second));
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.Equal("Second Persona", roster.FindControl<TextBlock>("TxtCompanion2Prompt")!.Text);
            Assert.Equal("", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            CloseConfirmation(shell);

            // Active companion 0: activated at once.
            shell.PersonalityFileForTests = _ => Task.FromResult<string?>(Fixture("First Persona"));
            Click(shell, roster.FindControl<Button>("BtnCompanion0Personality")!, MouseButton.Left);
            var first = s.GetCompanionPromptId(0);
            Assert.Equal(first, s.ActiveCommunityPromptId);
            Assert.Equal("fixture First Persona", s.CompanionPrompt.Personality);
            Assert.Equal("First Persona", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            CloseConfirmation(shell);

            // Switching to companion 2 activates her assignment (WPF SwitchCompanion :214).
            Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Left);
            Assert.Equal(second, s.ActiveCommunityPromptId);
            Assert.Equal("fixture Second Persona", s.CompanionPrompt.Personality);
        }
        finally
        {
            foreach (var id in s.InstalledCommunityPromptIds)
                File.Delete(Path.Combine(CorePaths.UserData, "community-prompts", id + ".json"));
            s.CompanionPromptAssignments = oldAssignments;
            s.InstalledCommunityPromptIds = oldInstalled;
            s.ActiveCommunityPromptId = oldActivePrompt;
            s.CompanionPrompt = oldPrompt;
            Directory.Delete(dir, true);
        }
    });

    private static void CloseConfirmation(MainShellWindow shell)
    {
        var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        dialog.Close(true);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task WithRoster(Action<MainShellWindow, CompanionRoomView, WorkshopRosterCell> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var oldMotion = s.MotionLevel;
        var oldActive = s.ActiveCompanionId;
        var oldAvatar = s.AvatarEnabled;
        var oldSet = s.SelectedAvatarSet;
        var oldModSets = new System.Collections.Generic.Dictionary<string, int>(s.ModAvatarSet);
        var oldSlutMode = s.SlutModeEnabled;
        var oldProgress = s.CompanionProgressData;
        s.CompanionProgressData = new();
        s.MotionLevel = MotionLevel.Off;
        s.ActiveCompanionId = 0;
        s.SelectedAvatarSet = 1;
        s.AvatarEnabled = false;
        s.SlutModeEnabled = false;
        for (int i = 0; i < 5; i++) s.CompanionProgressData[i] = new CompanionProgress { Level = i + 2 };
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var room = shell.GetVisualDescendants().OfType<CompanionRoomView>().Single();
            var hero = room.GetVisualDescendants().OfType<CompanionHeroCard>().Single();
            var switchButton = hero.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, hero.ViewModel!.SwitchCommand));
            Click(shell, switchButton, MouseButton.Left); // the user's Switch chip opens Roster
            Assert.True(((WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!).IsExpanded);
            var roster = room.GetVisualDescendants().OfType<WorkshopRosterCell>().Single();
            test(shell, room, roster);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.ActiveCompanionId = oldActive;
            s.AvatarEnabled = oldAvatar;
            s.SelectedAvatarSet = oldSet;
            s.ModAvatarSet = oldModSets;
            s.SlutModeEnabled = oldSlutMode;
            s.CompanionProgressData = oldProgress;
            s.MotionLevel = oldMotion;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    private static void Click(MainShellWindow shell, Control control, MouseButton button)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), shell)!.Value;
        shell.MouseDown(at, button);
        shell.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }
}
