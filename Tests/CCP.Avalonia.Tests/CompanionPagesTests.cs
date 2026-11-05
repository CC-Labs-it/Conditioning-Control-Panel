using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF v2 (CompanionTabView.xaml.cs:36-40, 5681c132a): the room is collapsed behind the
/// ConversationPage, and Companion > Personality / Permissions / Links are pages that adopt the
/// room's live zones (CompanionPageHost.Adopt), reached from the pills, the chat's Settings button
/// and the Workshop cell -> page map.
/// </summary>
public sealed class CompanionPagesTests
{
    [Fact]
    public Task CollapsedRoomLendsItsZonesToThePages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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

        var w = new MainShellWindow { Width = 1400, Height = 800 };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var strip = w.Named<SectionTabStrip>("SectionStrip")!;
            var tab = w.Named<CompanionTabView>("CompanionTab")!;
            var room = tab.FindControl<CompanionRoomView>("Room")!;
            var chat = tab.FindControl<ConversationPage>("Conversation")!;
            var workshop = (WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!;
            var grid = room.FindControl<AiPermissionsGrid>("PermissionsZone")!;
            var presets = room.FindControl<MakeHerYoursView>("PersonalityZone")!;

            // The chat page is the tab; the room is collapsed and the page is not inside it.
            w.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.False(room.IsVisible);
            Assert.True(chat.IsEffectivelyVisible);
            Assert.False(room.IsVisualAncestorOf(chat));
            Assert.Contains("personality", strip.PillKeys);
            Assert.Contains("permissions", strip.PillKeys);
            Assert.Contains("companionlinks", strip.PillKeys);

            // A user click on the pill, as the strip raises it: the page shows with the live grid.
            strip.PillFor("permissions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var permissions = w.Named<PermissionsPage>("CompanionPermissionsTab")!;
            Assert.Equal(("permissions", "permissions"), (w.CurrentTab, strip.ActivePillKey));
            Assert.True(permissions.IsVisible);
            Assert.False(tab.IsVisible);
            Assert.True(permissions.IsVisualAncestorOf(grid));
            Capture(w, "permissions");

            // Its Takeover link (WPF LinkOpenTakeover) opens Takeover.
            permissions.FindControl<Button>("LinkOpenTakeover")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("bambitakeover", w.CurrentTab);
            // ...and Takeover links back (WPF TxtPermissionsLiveInCompanion).
            w.Named<BambiTakeoverTabView>("BambiTakeoverTab")!.FindControl<Button>("TxtPermissionsLiveInCompanion")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("permissions", w.CurrentTab);

            w.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            strip.PillFor("companionlinks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("companionlinks", w.CurrentTab);
            Assert.True(w.Named<LinksPage>("CompanionLinksTab")!.IsVisualAncestorOf(workshop.Parts.Library));
            Capture(w, "companionlinks");

            // The chat's Settings button opens Personality (WPF OpenSheet -> ShowTab), which hosts
            // the presets zone and the Community cell.
            w.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            chat.FindControl<Button>("SettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var personality = w.Named<PersonalityPage>("CompanionPersonalityTab")!;
            Assert.Equal("personality", w.CurrentTab);
            Assert.True(personality.IsVisualAncestorOf(presets));
            Assert.True(personality.IsVisualAncestorOf(workshop.Parts.Community));
            Capture(w, "personality");

            // The Advanced row's door (WPF BtnOpenPromptEditor) opens the full prompt editor.
            personality.FindControl<Button>("BtnOpenPromptEditor")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(w.OwnedWindows.OfType<CompanionPromptEditorDialog>());
            editor.Close();
            Dispatcher.UIThread.RunJobs();

            // Adopted, not borrowed: the zones stay on their pages when the user goes back to chat.
            w.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.True(permissions.IsVisualAncestorOf(grid));
            Assert.True(personality.IsVisualAncestorOf(presets));

            // The Workshop cell -> page map (WPF RevealCompanionWorkshopCell, the tours' door).
            w.RevealCompanionWorkshopCell(CompanionRoomAnchors.WorkshopBehaviorCell);
            Assert.Equal("companionai", w.CurrentTab);
            w.RevealCompanionWorkshopCell(CompanionRoomAnchors.WorkshopLibraryCell);
            Assert.Equal("companionlinks", w.CurrentTab);
            w.RevealCompanionWorkshopCell(null);
            Assert.Equal("personality", w.CurrentTab);
        }
        finally
        {
            s.CompanionSectionOpen.Clear();
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            w.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    /// <summary>Evidence frames when CCP_COMPANION_PAGES_RENDER names a directory.</summary>
    private static void Capture(MainShellWindow w, string page)
    {
        if (System.Environment.GetEnvironmentVariable("CCP_COMPANION_PAGES_RENDER") is not { Length: > 0 } dir) return;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(dir, page + ".png"));
    }
}
