using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
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
            // WPF MainWindow.CompanionTabs.cs:20 and SectionTabStrip.xaml.cs:228, :629:
            // the visible AI pill has a leading glyph and opens the registered page.
            var aiPill = shell.PageStrip!.PillFor("companionai")!;
            Assert.Single(aiPill.GetLogicalDescendants().OfType<IconGlyph>());
            aiPill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.IsVisible);
            Assert.Equal("companionai", shell.CurrentTab);
            Assert.All(zones, z => Assert.True(page.IsVisualAncestorOf(z)));
            Assert.Equal("probe-model", ((EngineRoomVm)engine.DataContext!).OllamaModel);
            Assert.True(((EngineRoomVm)engine.DataContext!).IsExpanded);   // WPF: Engine.IsExpanded = true

            // The section strip draws the pill (it only draws pages this head can open).
            var strip = shell.PageStrip!;
            Assert.Contains(strip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("label_ai_badge"));

            // WPF AiPage.xaml:30-32 and AiPage.xaml.cs:26-28: name, recap, then diary;
            // every visit rebuilds the first two so an account change cannot leave stale editors.
            var nameHost = page.FindControl<ContentControl>("NameHost")!;
            var recapHost = page.FindControl<ContentControl>("RecapHost")!;
            var memoryHost = page.FindControl<ContentControl>("MemoryHost")!;
            var name = Assert.IsType<PreferredNameEditor>(nameHost.Content);
            var recap = Assert.IsType<ConversationRecap>(recapHost.Content);
            var memoryStack = Assert.IsType<StackPanel>(nameHost.Parent);
            Assert.Equal(new Control[] { nameHost, recapHost, memoryHost }, memoryStack.Children.Skip(2));

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
            Assert.IsType<PreferredNameEditor>(nameHost.Content);
            Assert.IsType<ConversationRecap>(recapHost.Content);
            Assert.NotSame(name, nameHost.Content);
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
