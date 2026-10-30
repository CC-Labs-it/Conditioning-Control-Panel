using System;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
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
        roster.PersonalityAssignRequested += (_, index) => { Assert.Equal(2, index); personalityRequests++; };
        Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Right);
        Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
        Click(shell, roster.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
        Assert.Equal(1, personalityRequests);
        Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
    });

    [Fact]
    public Task PersonalityButtonReachesPickerAndCancelKeepsAssignment() => WithRoster((shell, room, roster) =>
    {
        int picks = 0;
        shell.PersonalityFileForTests = () => { picks++; return Task.FromResult<string?>(null); };
        var before = CoreSettings.Current.GetCompanionPromptId(2);
        Click(shell, roster.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
        Assert.Equal(1, picks);
        Assert.Equal(before, CoreSettings.Current.GetCompanionPromptId(2));
    });

    [Fact]
    public Task AssignmentAppliesActiveImmediatelyAndInactiveOnSwitchBeforeEvent() => WithRoster((shell, room, roster) =>
    {
        var folder = System.IO.Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
        System.IO.Directory.CreateDirectory(folder);
        var id = "assignment-" + Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(folder, id + ".json");
        System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new CommunityPrompt
        { Id = id, Name = "Assigned persona", PromptSettings = new() { Personality = "fixture persona" } }));
        try
        {
            shell.AssignImportedPersonality(0, id, room);
            Assert.Equal(id, CoreSettings.Current.GetCompanionPromptId(0));
            Assert.Equal(id, CoreSettings.Current.ActiveCommunityPromptId);
            Assert.Equal("Assigned persona", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
            CoreSettings.Current.ActiveCommunityPromptId = null;
            shell.AssignImportedPersonality(2, id, room);
            Assert.Null(CoreSettings.Current.ActiveCommunityPromptId);
            string? atSwitch = null;
            void Switched(CompanionId _) => atSwitch = CoreSettings.Current.ActiveCommunityPromptId;
            CompanionCore.Switched += Switched;
            try { Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Left); }
            finally { CompanionCore.Switched -= Switched; }
            Assert.Equal(id, atSwitch);
            Assert.Contains("Assigned persona", room.PersonalityZone.ViewModel.ActivePersonalityLine);
            Assert.Equal("fixture persona", CoreSettings.Current.CompanionPrompt.Personality);
            CoreSettings.Current.SetCompanionPromptId(4, null);
            Click(shell, roster.FindControl<Border>("CompanionCard4")!, MouseButton.Left);
            Assert.Equal(id, CoreSettings.Current.ActiveCommunityPromptId); // WPF keeps the current prompt.
            CoreSettings.Current.SetCompanionPromptId(0, "missing-file");
            Click(shell, roster.FindControl<Border>("CompanionCard0")!, MouseButton.Left);
            Assert.Equal(id, CoreSettings.Current.ActiveCommunityPromptId);
        }
        finally { System.IO.File.Delete(path); }
    });

    [Fact]
    public Task TabShowRereadsTriggerAndBehaviorSettings() => WithRoster((shell, room, roster) =>
    {
        shell.ShowTab("settings");
        CoreSettings.Current.TriggerModeEnabled = true;
        CoreSettings.Current.TriggerIntervalSeconds = 85;
        CoreSettings.Current.IdleGiggleIntervalSeconds = 180;
        CoreSettings.Current.BubbleDurationSeconds = 4;
        shell.ShowTab("companion");
        Dispatcher.UIThread.RunJobs();
        var triggers = room.WorkshopParts!.Triggers;
        Assert.True(triggers.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked);
        Assert.True(triggers.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
        Assert.Equal(85, triggers.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value);
        Assert.Equal("85s", triggers.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text);
        var behavior = room.WorkshopParts.Behavior;
        Assert.Equal(180, behavior.FindControl<Slider>("SliderIdleIntervalCompanion")!.Value);
        Assert.Equal(4, behavior.FindControl<Slider>("SliderBubbleDurationCompanion")!.Value);
        Assert.Equal(180, CoreSettings.Current.IdleGiggleIntervalSeconds);
        Assert.Equal(4, CoreSettings.Current.BubbleDurationSeconds);
    });

    [Fact]
    public Task PanicClosesCommunityModal() => WithRoster((shell, room, roster) =>
    {
        var dialog = new ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog("Community", "Fixture", false);
        var pending = WorkshopCommunityCell.ShowPromptDialogAsync(shell, dialog);
        Assert.True(dialog.IsVisible);
        PanicSurfaces.StopAll("test", shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.IsVisible);
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.False(pending.Result);
    });

    [Fact]
    public Task PanicDuringBrowseFetchPreventsLateDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        CoreSettings.Current.OfflineMode = false;
        var handler = new PendingManifest();
        using var http = new System.Net.Http.HttpClient(handler);
        using var service = new CommunityPromptService(http);
        var owner = new Window();
        owner.Show();
        int ownerReads = 0;
        try
        {
            var pending = WorkshopCommunityCell.BrowseAsync(() => { ownerReads++; return owner; }, service, () => { });
            Assert.False(pending.IsCompleted);
            PanicSurfaces.StopAll("test");
            handler.Response.SetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("{\"Prompts\":[]}") });
            await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(pending.IsCompleted, "Browse reopened a modal after panic");
            await pending;
            Assert.Equal(0, ownerReads);
        }
        finally { owner.Close(); }
    });

    private sealed class PendingManifest : System.Net.Http.HttpMessageHandler
    {
        internal readonly TaskCompletionSource<System.Net.Http.HttpResponseMessage> Response = new();
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken) => Response.Task;
    }

    [Fact]
    public Task PersonalityPickerImportUpdatesRealAssignment() => WithRoster((shell, room, roster) =>
    {
        var path = System.IO.Path.Combine(CorePaths.UserData, "Picked Persona.json");
        System.IO.File.WriteAllText(path, "{\"Name\":\"Picked persona\",\"PromptSettings\":{\"Personality\":\"fixture\"}}");
        string? id = null;
        try
        {
            shell.PersonalityFileForTests = () => Task.FromResult<string?>(path);
            Click(shell, roster.FindControl<Button>("BtnCompanion0Personality")!, MouseButton.Left);
            id = CoreSettings.Current.GetCompanionPromptId(0);
            Assert.False(string.IsNullOrEmpty(id));
            Assert.Equal(id, CoreSettings.Current.ActiveCommunityPromptId);
            Assert.Equal("Picked Persona", roster.FindControl<TextBlock>("TxtCompanion0Prompt")!.Text);
        }
        finally
        {
            WorkshopCommunityCell.StopForPanic();
            System.IO.File.Delete(path);
            if (id != null) room.WorkshopParts!.Community.Service.RemovePrompt(id);
        }
    });

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
            var room = CollapsedRoom.Show(shell);   // v2 collapses the room; the roster lives in its Workshop drawer
            Dispatcher.UIThread.RunJobs();
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
