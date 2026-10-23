using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 48f587200 / 9db154ab6: Companion > AI is a pill of the Companion section and shows the
/// room's live Connection drawer (expanded), the Behaviour + Triggers cells and the memory diary. The
/// room is collapsed (v2), so the page adopts them for good (WPF CompanionPageHost.Adopt).</summary>
public sealed class CompanionAiPageTests
{
    [Fact]
    public void MissingRoomStillBuildsFreshNameAndRecap() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousBrain = global::ConditioningControlPanel.Avalonia.App.Brain;
        global::ConditioningControlPanel.Avalonia.App.Brain = null;
        try
        {
            var page = new AiPage();
            page.OnShown(null);   // WPF creates these before looking for the collapsed room
            var nameHost = page.FindControl<ContentControl>("NameHost")!;
            var recapHost = page.FindControl<ContentControl>("RecapHost")!;
            var name = Assert.IsType<PreferredNameEditor>(nameHost.Content);
            var recap = Assert.IsType<ConversationRecap>(recapHost.Content);
            Assert.False(name.NameBox.IsEnabled);
            Assert.False(name.SaveButton.IsEnabled);
            Assert.False(recap.IsVisible);
            page.OnShown(null);
            Assert.NotSame(name, nameHost.Content);
            Assert.NotSame(recap, recapHost.Content);
            Assert.Null(page.FindControl<ContentControl>("EngineHost")!.Content);
            Assert.Null(page.FindControl<ContentControl>("MemoryHost")!.Content);
        }
        finally { global::ConditioningControlPanel.Avalonia.App.Brain = previousBrain; }
    });

    [Fact]
    public System.Threading.Tasks.Task AiPillAdoptsTheLiveZones() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52
        s.CompanionSectionOpen.Clear();
        s.AwarenessModeEnabled = false;                         // no v2 consent dialog on the tab

        var oldModel = s.CompanionPrompt.AiModel;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var accordion = room.FindControl<WorkshopAccordion>("WorkshopZone")!;
            var workshop = (WorkshopRuntimeVm)accordion.DataContext!;
            var page = shell.GetLogicalDescendants().OfType<AiPage>().Single();

            // The default: Workshop drawer closed. The page still gets the cells, and re-reads the
            // Connection settings changed elsewhere (WPF tab.Vm.Sync).
            Dispatcher.UIThread.RunJobs();
            s.CompanionPrompt.AiModel = "probe-model";
            var engine = room.FindControl<EngineRoomDrawer>("EngineZone")!;
            var diary = room.FindControl<MemoryDiaryView>("MemoryZone")!;
            Control[] zones = { engine, workshop.Parts.Behavior, workshop.Parts.Triggers, diary };
            // The real Companion pill opens the page, including the formerly missing glyph.
            var strip = shell.PageStrip!;
            var pill = strip.PillFor("companionai")!;
            var glyph = strip.Part("companionai")!.Glyph!;
            Assert.NotNull(glyph);
            Assert.Equal(FluentIcons.Common.Symbol.Bot, glyph.Kind);
            Assert.False(glyph.IsFallback);
            Assert.True(pill.IsVisualAncestorOf(glyph));
            pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.IsVisible);
            Assert.Equal("companionai", shell.CurrentTab);
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            Assert.Equal("probe-model", ((EngineRoomVm)engine.DataContext!).OllamaModel);
            Assert.True(((EngineRoomVm)engine.DataContext!).IsExpanded);   // WPF: Engine.IsExpanded = true

            Assert.Equal("companionai", strip.ActivePillKey);
            Assert.Contains(pill.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("label_ai_badge"));
            Assert.Same(engine, page.FindControl<ContentControl>("EngineHost")!.Content);
            Assert.Same(workshop.Parts.Behavior, page.FindControl<ContentControl>("BehaviorHost")!.Content);
            Assert.Same(workshop.Parts.Triggers, page.FindControl<ContentControl>("TriggersHost")!.Content);
            Assert.Same(diary, page.FindControl<ContentControl>("MemoryHost")!.Content);
            var nameHost = page.FindControl<ContentControl>("NameHost")!;
            var recapHost = page.FindControl<ContentControl>("RecapHost")!;
            var name = Assert.IsType<PreferredNameEditor>(nameHost.Content);
            var recap = Assert.IsType<ConversationRecap>(recapHost.Content);
            // WPF AiPage.xaml: name, recap, then the live diary, in the same memory sheet.
            Assert.Equal(new[] { nameHost, recapHost, page.FindControl<ContentControl>("MemoryHost")! },
                ((StackPanel)nameHost.Parent!).Children.OfType<ContentControl>());

            // Leaving keeps them (the room is collapsed) and disarms an armed forget (WPF OnHidden).
            typeof(DiaryForgetConfirm).GetProperty(nameof(DiaryForgetConfirm.IsArmed))!.SetValue(diary.ForgetConfirm, true);
            Assert.True(diary.ForgetConfirm.IsArmed);
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.False(diary.ForgetConfirm.IsArmed);
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            Assert.False(room.IsVisible);

            // A second visit is a no-op adopt; the drawer is expanded again.
            ((EngineRoomVm)engine.DataContext!).IsExpanded = false;
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            Assert.True(((EngineRoomVm)engine.DataContext!).IsExpanded);
            Assert.NotSame(name, nameHost.Content);   // WPF refreshes both on every visit
            Assert.NotSame(recap, recapHost.Content);
            await System.Threading.Tasks.Task.CompletedTask;
        }
        finally
        {
            s.CompanionSectionOpen.Clear();
            s.CompanionPrompt.AiModel = oldModel;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
    });
}
