using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
            var dust = chat.FindControl<global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas>("StageDust");
            Assert.NotNull(dust);
            Assert.False(dust.IsHitTestVisible);
            var config = (global::ConditioningControlPanel.Avalonia.Controls.AmbientFxConfig)typeof(global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas)
                .GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(dust)!;
            Assert.Equal(global::ConditioningControlPanel.Avalonia.Controls.AmbientFxLayers.DustField, config.Layers);
            Assert.Equal(0.8, config.Intensity);
            Assert.Equal(0.65, config.DustDensity);
            Assert.Equal(global::Avalonia.Media.Color.FromRgb(244, 134, 211), config.Tint);
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
            Assert.IsType<CompanionPickerCard>(personality.FindControl<ContentControl>("PickerHost")!.Content);
            Assert.False(dust.IsRunning); // leaving chat parks the shared ambient clock
            Assert.True(personality.IsVisualAncestorOf(presets));
            Assert.True(personality.IsVisualAncestorOf(workshop.Parts.Community));
            Capture(w, "personality");
            var picker = (CompanionPickerCard)personality.FindControl<ContentControl>("PickerHost")!.Content!;
            s.ActivePersonalityPresetId = ConditioningControlPanel.Models.PersonalityPresets.NeutralDefaultId;
            s.CompanionPrompt.UseCustomPrompt = false;
            var count = s.UserPersonalityPresets.Count;
            picker.FindControl<Button>("BtnMorePersonality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var studio = Assert.Single(w.OwnedWindows.Where(x => x.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "Save")));
            var fields = studio.GetLogicalDescendants().OfType<TextBox>().ToList();
            var name = fields.Single(x => x.Name == "name");
            var voice = fields.Single(x => x.Name == "voice");
            name.Text = "";
            Assert.False(studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Save").IsEnabled);
            name.Text = "Studio test";
            voice.Text = "A test voice";
            fields.Single(x => x.Name == "style").Text = null;
            fields.Single(x => x.Name == "awareness").Text = "Test awareness";
            fields.Single(x => x.Name == "samples").Text = "first sample\nsecond sample\nthird sample";
            studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count + 1, s.UserPersonalityPresets.Count);
            var saved = s.UserPersonalityPresets.Last();
            Assert.Equal("A test voice", saved.PromptSettings.Personality);
            Assert.Equal("", saved.PromptSettings.OutputRules);
            Assert.Equal("Test awareness", saved.PromptSettings.ContextReactions);
            Assert.Equal(2, saved.SampleLines!.Count);
            picker.FindControl<Button>("BtnMorePersonality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            studio = Assert.Single(w.OwnedWindows.Where(x => x.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "Save")));
            Assert.NotNull(studio.GetLogicalDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "Delete"));
            studio.GetLogicalDescendants().OfType<TextBox>().Single(x => x.Name == "name").Text = "Updated test";
            studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count + 1, s.UserPersonalityPresets.Count);
            Assert.Equal("Updated test", saved.Name);
            picker.FindControl<Button>("BtnMorePersonality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            studio = Assert.Single(w.OwnedWindows.Where(x => x.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "Save")));
            studio.GetLogicalDescendants().OfType<TextBox>().Single(x => x.Name == "voice").Text = "Discard this";
            PersonalityStudio.CloseForPanic();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("A test voice", saved.PromptSettings.Personality);
            Assert.False(studio.IsVisible);
            picker.FindControl<Button>("BtnMorePersonality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            studio = Assert.Single(w.OwnedWindows.Where(x => x.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "Save")));
            studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var confirm = Assert.Single(studio.OwnedWindows.OfType<MessageDialog>());
            confirm.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(saved, s.UserPersonalityPresets);
            studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(studio.OwnedWindows.OfType<MessageDialog>()).FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(saved, s.UserPersonalityPresets);

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

            // WPF cap watch never interrupts the conversation, then files once after leaving it.
            var capKey = "conv:" + ConditioningControlPanel.Services.Companion.CompanionCapRules.KeyCompanionCap;
            var stamps = s.InboxStamps;
            stamps.Remove(ConditioningControlPanel.Services.Companion.CompanionCapRules.KeyCompanionCap);
            global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Remove(capKey);
            w.TryFileCompanionCap();
            Assert.False(global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Contains(capKey));
            w.ShowTab("personality");
            w.TryFileCompanionCap();
            Assert.True(global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Contains(capKey));
            var capRow = global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Items.Single(i => i.Key == capKey);
            w.TryFileCompanionCap();
            Assert.Same(capRow, global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Items.Single(i => i.Key == capKey));
            capRow.Open();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.CloseCapNotice();
            global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Remove(capKey);

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
