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
    public Task AssignedPromptLabelAndRosterSwitchReachTheLiveVoice() => WithRoster((shell, room, roster) =>
    {
        var s = CoreSettings.Current;
        var oldId = s.GetCompanionPromptId(2);
        var oldPrompt = s.CompanionPrompt;
        var oldActive = s.ActiveCommunityPromptId;
        var folder = System.IO.Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
        System.IO.Directory.CreateDirectory(folder);
        var id = "test-roster-" + Guid.NewGuid().ToString("N");
        var file = System.IO.Path.Combine(folder, id + ".json");
        System.IO.File.WriteAllText(file, "{\"Name\":\"Kind voice\",\"PromptSettings\":{\"Personality\":\"be kind\"}}");
        try
        {
            s.SetCompanionPromptId(2, id);
            roster.Refresh();
            var label = roster.FindControl<TextBlock>("TxtCompanion2Prompt")!;
            Assert.Equal("Kind voice", label.Text);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("tooltip_ai_personality", "Kind voice"), ToolTip.GetTip(label));
            // A mod-forced switch (CoreModsHooks.SwitchCompanion -> CompanionCore) applies it too (WPF CompanionService.cs:212).
            CompanionCore.SwitchCompanion((CompanionId)2);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, s.ActiveCommunityPromptId);
            Assert.Equal("be kind", s.CompanionPrompt.Personality);
            CompanionCore.SwitchCompanion(CompanionId.OGBambiSprite);
            Dispatcher.UIThread.RunJobs();
            s.CompanionPrompt = oldPrompt;
            s.ActiveCommunityPromptId = oldActive;
            Click(shell, roster.FindControl<Border>("CompanionCard2")!, MouseButton.Left);
            Assert.Equal(id, s.ActiveCommunityPromptId);
            Assert.Equal("be kind", s.CompanionPrompt.Personality);
            s.SetCompanionPromptId(2, "missing");
            roster.Refresh();
            Assert.Equal("", label.Text);
            Assert.Null(ToolTip.GetTip(label));
        }
        finally
        {
            s.SetCompanionPromptId(2, oldId);
            s.CompanionPrompt = oldPrompt;
            s.ActiveCommunityPromptId = oldActive;
            System.IO.File.Delete(file);
        }
    });

    [Fact]
    public Task PersonalityButtonCancelsImportsAndIgnoresCorruptFiles() => WithRoster((shell, room, roster) =>
    {
        var s = CoreSettings.Current;
        var oldId = s.GetCompanionPromptId(3);
        var oldPrompt = s.CompanionPrompt;
        var oldPick = MainShellWindow.PickPromptFile;
        var oldInform = MainShellWindow.InformPrompt;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-prompt-").FullName;
        string? picked = null;
        var told = new System.Collections.Generic.List<string>();
        MainShellWindow.PickPromptFile = (_, _) => Task.FromResult(picked);
        MainShellWindow.InformPrompt = (_, title, _) => { told.Add(title); return Task.CompletedTask; };
        void Press() { roster.FindControl<Button>("BtnCompanion3Personality")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            s.SetCompanionPromptId(3, null);
            Press();   // cancelled picker: nothing assigned, nothing said
            Assert.True(string.IsNullOrEmpty(s.GetCompanionPromptId(3)));
            Assert.Empty(told);

            picked = System.IO.Path.Combine(dir, "Sharp voice.json");
            System.IO.File.WriteAllText(picked, "{\"Name\":\"x\",\"PromptSettings\":{\"Personality\":\"be sharp\"}}");
            Press();
            Assert.False(string.IsNullOrEmpty(s.GetCompanionPromptId(3)));
            Assert.Equal("Sharp voice", roster.FindControl<TextBlock>("TxtCompanion3Prompt")!.Text);
            Assert.Equal(new[] { ConditioningControlPanel.Localization.Loc.Get("title_personality_assigned") }, told);
            Assert.Equal(CompanionId.OGBambiSprite, CompanionCore.ActiveCompanion);
            Assert.Same(oldPrompt, s.CompanionPrompt);   // not the active card: the live voice is untouched

            System.IO.File.WriteAllText(picked, "{ not json");
            Press();
            Assert.Single(told); // WPF ImportFromFile returns null on corrupt JSON.
        }
        finally
        {
            var installed = s.GetCompanionPromptId(3);
            if (!string.IsNullOrEmpty(installed))
            {
                s.InstalledCommunityPromptIds.Remove(installed);
                System.IO.File.Delete(System.IO.Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName, installed + ".json"));
            }
            s.SetCompanionPromptId(3, oldId);
            s.CompanionPrompt = oldPrompt;
            MainShellWindow.PickPromptFile = oldPick;
            MainShellWindow.InformPrompt = oldInform;
            System.IO.Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task AssigningActiveCompanionAppliesImmediatelyAndClosedShellUnsubscribes() => WithRoster((shell, room, roster) =>
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        System.IO.File.WriteAllText(file, "{\"PromptSettings\":{\"Personality\":\"active voice\"}}");
        MainShellWindow.PickPromptFile = (_, _) => Task.FromResult<string?>(file);
        var inform = MainShellWindow.InformPrompt;
        MainShellWindow.InformPrompt = (_, _, _) => Task.CompletedTask;
        try
        {
            roster.FindControl<Button>("BtnCompanion0Personality")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("active voice", CoreSettings.Current.CompanionPrompt.Personality);
            Assert.Equal(CoreSettings.Current.GetCompanionPromptId(0), CoreSettings.Current.ActiveCommunityPromptId);
            CoreSettings.Current.SetCompanionPromptId(2, CoreSettings.Current.GetCompanionPromptId(0));
            shell.Close();
            CoreSettings.Current.CompanionPrompt = new();
            CompanionCore.SwitchCompanion((CompanionId)2);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual("active voice", CoreSettings.Current.CompanionPrompt.Personality);
        }
        finally
        {
            MainShellWindow.InformPrompt = inform;
            System.IO.File.Delete(file);
        }
    });

    [Fact]
    public Task NullPromptSettingsCannotBeAssignedOrApplied() => WithRoster((shell, room, roster) =>
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json");
        System.IO.File.WriteAllText(file, "{\"PromptSettings\":null}");
        MainShellWindow.PickPromptFile = (_, _) => Task.FromResult<string?>(file);
        var inform = MainShellWindow.InformPrompt;
        MainShellWindow.InformPrompt = (_, _, _) => Task.CompletedTask;
        var s = CoreSettings.Current;
        s.SetCompanionPromptId(2, null);
        var previous = s.CompanionPrompt;
        var count = s.InstalledCommunityPromptIds.Count;
        try
        {
            roster.FindControl<Button>("BtnCompanion2Personality")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(string.IsNullOrEmpty(s.GetCompanionPromptId(2)));
            Assert.Equal(count, s.InstalledCommunityPromptIds.Count);
            // A previously stored invalid assignment also leaves the current voice alone.
            var prompt = room.WorkshopParts!.Community.Service.ImportFromFile(file)!;
            s.SetCompanionPromptId(2, prompt.Id);
            CompanionCore.SwitchCompanion((CompanionId)2);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(previous, s.CompanionPrompt);
        }
        finally { MainShellWindow.InformPrompt = inform; System.IO.File.Delete(file); }
    });

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
    public Task ForcedSwitchRefreshesVisibleCommunityIndicator() => WithRoster((shell, room, roster) =>
    {
        var s = CoreSettings.Current;
        s.InstalledCommunityPromptIds.Clear();
        s.ActiveCommunityPromptId = null;
        var file = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(file, "{\"Name\":\"Voice\",\"PromptSettings\":{\"Personality\":\"kind\"}}");
            var prompt = room.WorkshopParts!.Community.Service.ImportFromFile(file)!;
            s.SetCompanionPromptId(2, prompt.Id);
            shell.ShowTab("personality");
            Dispatcher.UIThread.RunJobs();
            var panel = room.WorkshopParts.Community.FindControl<StackPanel>("InstalledPromptsPanel")!;
            Assert.True(panel.IsEffectivelyVisible);
            Assert.Empty(panel.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>());
            CompanionCore.SwitchCompanion((CompanionId)2);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(prompt.Id, s.ActiveCommunityPromptId);
            Assert.Single(panel.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>());
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), b => b.Name == "ActivatePrompt");
        }
        finally { System.IO.File.Delete(file); }
    });

    [Fact]
    public Task QueuedSwitchCannotApplyPromptAfterClose() => WithRoster((shell, room, roster) =>
    {
        var file = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(file, "{\"PromptSettings\":{\"Personality\":\"queued voice\"}}");
            var prompt = room.WorkshopParts!.Community.Service.ImportFromFile(file)!;
            var s = CoreSettings.Current;
            s.SetCompanionPromptId(2, prompt.Id);
            var previous = s.CompanionPrompt;
            var previousId = s.ActiveCommunityPromptId;
            CompanionCore.SwitchCompanion((CompanionId)2);
            Assert.Same(previous, s.CompanionPrompt); // callback is still queued
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(previous, s.CompanionPrompt);
            Assert.Equal(previousId, s.ActiveCommunityPromptId);
        }
        finally { System.IO.File.Delete(file); }
    });

    [Theory]
    [InlineData("en","Select AI personality for {0}","Prompt JSON files")]
    [InlineData("de","KI-Persönlichkeit für {0} auswählen","JSON-Promptdateien")]
    [InlineData("es","Seleccionar personalidad de IA para {0}","Archivos JSON de instrucciones")]
    [InlineData("fr","Choisir une personnalité IA pour {0}","Fichiers JSON de consignes")]
    [InlineData("it","Seleziona la personalità IA per {0}","File JSON di istruzioni")]
    [InlineData("ja","{0} の AI パーソナリティを選択","プロンプト JSON ファイル")]
    [InlineData("ko","{0}의 AI 성격 선택","프롬프트 JSON 파일")]
    [InlineData("pt-BR","Selecionar personalidade de IA para {0}","Arquivos JSON de instruções")]
    [InlineData("ru","Выберите личность ИИ для {0}","Файлы промптов JSON")]
    [InlineData("zh-CN","为 {0} 选择 AI 人格","提示词 JSON 文件")]
    public Task PersonalityPickerUsesLocalizedTitleAndFilter(string language, string title, string filter) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            var options = MainShellWindow.CompanionPromptPickerOptions(2);
            Assert.Equal(string.Format(title, CompanionDefinition.GetById(2).Name), options.Title);
            var type = Assert.Single(options.FileTypeFilter!);
            Assert.Equal(filter, type.Name);
            Assert.Equal("*.json", Assert.Single(type.Patterns!));
            Assert.False(options.AllowMultiple);
        }
        finally { loc.SetLanguage(previous); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RosterFixtureRestoresPromptStateAcrossConsecutiveRuns() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previous = CoreSettings.ServiceProvider;
        var seed = new SettingsService();
        CoreSettings.ServiceProvider = () => seed;
        try
        {
            seed.Current.CompanionPromptAssignments = new() { [0] = "before-zero", [2] = "before-two" };
            seed.Current.CompanionPrompt = new() { Personality = "before-voice" };
            seed.Current.ActiveCommunityPromptId = "before-active";
            seed.Current.InstalledCommunityPromptIds = new() { "before-active", "before-installed" };
            seed.SaveImmediate();
            // No fact boundary between these calls: the assembly isolation hook cannot mask a leak.
            await WithRoster((shell, room, roster) =>
            {
                var s = CoreSettings.Current;
                s.SetCompanionPromptId(0, "after-zero");
                s.SetCompanionPromptId(2, "after-two");
                s.SetCompanionPromptId(3, "after-three");
                s.CompanionPrompt = new() { Personality = "after-voice" };
                s.ActiveCommunityPromptId = "after-active";
                s.InstalledCommunityPromptIds.Clear();
                s.InstalledCommunityPromptIds.Add("after-installed");
            });
            await WithRoster((shell, room, roster) =>
            {
                var s = CoreSettings.Current;
                Assert.Equal(2, s.CompanionPromptAssignments.Count);
                Assert.Equal("before-zero", s.GetCompanionPromptId(0));
                Assert.Equal("before-two", s.GetCompanionPromptId(2));
                Assert.Equal("before-voice", s.CompanionPrompt.Personality);
                Assert.Equal("before-active", s.ActiveCommunityPromptId);
                Assert.Equal(new[] { "before-active", "before-installed" }, s.InstalledCommunityPromptIds);
            });
        }
        finally { CoreSettings.ServiceProvider = previous; }
    });

    private static Task WithRoster(Action<MainShellWindow, CompanionRoomView, WorkshopRosterCell> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .WithInterFont().With(new global::Avalonia.Media.FontManagerOptions
                {
                    DefaultFamilyName = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter,
                    FontFamilyMappings = new System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>
                    {
                        ["Segoe UI"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter),
                        ["Segoe UI Black"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter)
                    }
                })
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var oldAssignments = new System.Collections.Generic.Dictionary<int, string>(s.CompanionPromptAssignments);
        var oldPrompt = s.CompanionPrompt;
        var oldPromptId = s.ActiveCommunityPromptId;
        var oldInstalled = s.InstalledCommunityPromptIds.ToList();
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
        var oldPicker = MainShellWindow.PickPromptFile;
        MainShellWindow.PickPromptFile = (_, _) => Task.FromResult<string?>(null);
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
            MainShellWindow.PickPromptFile = oldPicker;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.ActiveCompanionId = oldActive;
            s.AvatarEnabled = oldAvatar;
            s.SelectedAvatarSet = oldSet;
            s.ModAvatarSet = oldModSets;
            s.SlutModeEnabled = oldSlutMode;
            s.CompanionProgressData = oldProgress;
            s.MotionLevel = oldMotion;
            s.CompanionPromptAssignments = oldAssignments;
            s.CompanionPrompt = oldPrompt;
            s.ActiveCommunityPromptId = oldPromptId;
            s.InstalledCommunityPromptIds = oldInstalled;
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
