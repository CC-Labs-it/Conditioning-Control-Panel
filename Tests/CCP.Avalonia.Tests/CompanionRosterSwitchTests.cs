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
        // The cell now runs WPF BtnCompanionPersonality_Click itself (picker -> import -> assign to
        // THIS card's companion), so the request is observed as the picker opening once and the
        // prompt landing on companion 2 only.
        var oldPrompts = CompanionCore.Prompts;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-roster-switch-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        CompanionCore.Prompts = new CommunityPromptService(userDataPath: dir, validatorProvider: () => null);
        var file = System.IO.Path.Combine(dir, "Picked.json");
        System.IO.File.WriteAllText(file, "{\"PromptSettings\":{\"Personality\":\"Picked\"}}");
        var others = new[] { 0, 1, 3, 4 };
        var before = others.Select(i => CoreSettings.Current.GetCompanionPromptId(i)).ToArray();
        var before2 = CoreSettings.Current.GetCompanionPromptId(2);
        int personalityRequests = 0;
        roster.PickPromptFileForTests = _ => { personalityRequests++; return Task.FromResult<string?>(file); };
        try
        {
            Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Right);
            Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
            Click(shell, roster.FindControl<Button>("BtnCompanion2Personality")!, MouseButton.Left);
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();   // the confirmation box
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, personalityRequests);
            var assigned = CoreSettings.Current.GetCompanionPromptId(2);
            Assert.False(string.IsNullOrEmpty(assigned));
            Assert.NotEqual(before2, assigned);
            Assert.Equal(before, others.Select(i => CoreSettings.Current.GetCompanionPromptId(i)).ToArray());
            Assert.Equal(0, CoreSettings.Current.ActiveCompanionId);
        }
        finally
        {
            CoreSettings.Current.SetCompanionPromptId(2, before2);
            CompanionCore.Prompts.Dispose();
            CompanionCore.Prompts = oldPrompts;
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    });

    private static Task WithRoster(Action<MainShellWindow, CompanionRoomView, WorkshopRosterCell> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // WPF SwitchToCompanionAvatar pins a single-emote mod's avatar (AvatarTubeWindow.Avatar.cs:830), so
        // the tube's set only follows the card under a static-set mod: state that, don't inherit it.
        var oldModId = CoreMods.ActiveModIdProvider;
        var oldPackage = CoreMods.ActiveModPackageProvider;
        CoreMods.ActiveModIdProvider = () => "roster-static-test";
        CoreMods.ActiveModPackageProvider = () => null;
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
            CoreMods.ActiveModIdProvider = oldModId;
            CoreMods.ActiveModPackageProvider = oldPackage;
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
