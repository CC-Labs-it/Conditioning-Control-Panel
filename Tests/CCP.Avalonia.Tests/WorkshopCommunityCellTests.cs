using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopCommunityCellTests
{
    [Fact]
    public Task InstalledRowsRefreshActivateAndRemoveWithoutDestroyingLocalizedPlaceholder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.InstalledCommunityPromptIds.Clear();
        s.ActiveCommunityPromptId = null;
        s.SlutModeEnabled = false;
        var id = "cell-" + Guid.NewGuid().ToString("N");
        var folder = Path.Combine(CorePaths.UserData, "community-prompts");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".json"), JsonConvert.SerializeObject(new CommunityPrompt
        { Id = id, Name = "Test persona", Author = "Test author", PromptSettings = new() { Personality = "fixture personality" } }));
        s.InstalledCommunityPromptIds.Add(id);
        var cell = new WorkshopCommunityCell();
        var w = new Window { Width = 440, Height = 350, Content = cell };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var panel = cell.FindControl<StackPanel>("InstalledPromptsPanel")!;
            var placeholder = cell.FindControl<TextBlock>("TxtNoInstalledPrompts")!;
            Assert.False(placeholder.IsVisible);
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Test persona");
            var activate = panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ActivatePrompt");
            activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, s.ActiveCommunityPromptId);
            Assert.Equal("fixture personality", s.CompanionPrompt.Personality);
            Assert.True(s.CompanionPrompt.UseCustomPrompt);
            Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), b => b.Name == "ActivatePrompt");
            if (Environment.GetEnvironmentVariable("CCP_COMMUNITY_PROOF_DIR") is { } proof)
            {
                Directory.CreateDirectory(proof); w.UpdateLayout();
                using var frame = w.CaptureRenderedFrame(); frame!.Save(Path.Combine(proof, "community-installed-active.png"));
            }
            panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemovePrompt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.Empty(s.InstalledCommunityPromptIds);
            Assert.False(File.Exists(Path.Combine(folder, id + ".json")));
            Assert.Same(placeholder, cell.FindControl<TextBlock>("TxtNoInstalledPrompts"));
            Assert.True(placeholder.IsVisible);
            Assert.Equal(Loc.Get("label_no_prompts_installed_yet"), placeholder.Text);
        }
        finally { w.Close(); File.Delete(Path.Combine(folder, id + ".json")); }
        return Task.CompletedTask;
    });
    // Completion signal (P08/P73): the dialog's own Opened event, bounded by time, never a poll.
    private static Task<MessageDialog> Dialog(Window owner) => Opened<MessageDialog>(owner);
    private static async Task<T> Opened<T>(Window owner) where T : Window
    {
        var opened = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Avalonia 12.1 Window.ShowCore raises WindowOpenedEvent BEFORE it assigns Owner, so an Owner check
        // there never matches and a dialog opened after RunJobs (an awaited file write first) was never seen.
        // The instance Opened event (OnOpened) fires after Owner is set: check the owner there.
        using var sub = Window.WindowOpenedEvent.AddClassHandler<T>((d, _) =>
        {
            void Shown(object? s, EventArgs e) { d.Opened -= Shown; if (d.Owner == owner) opened.TrySetResult(d); }
            d.Opened += Shown;
        });
        Dispatcher.UIThread.RunJobs();
        if (owner.OwnedWindows.OfType<T>().FirstOrDefault() is { } open) return open;
        return await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public Task ActivatingAnExplicitPromptAsksForConsentAndCancelLeavesItInactive() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.InstalledCommunityPromptIds.Clear(); s.ActiveCommunityPromptId = null;
        s.SlutModeEnabled = true; s.CompanionPrompt.ExplicitContentAcknowledged = false;
        var id = "consent-" + Guid.NewGuid().ToString("N");
        var file = Path.Combine(CorePaths.UserData, "community-prompts", id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonConvert.SerializeObject(new CommunityPrompt
        { Id = id, Name = "Spicy persona", Author = "Test", PromptSettings = new() { SlutModePersonality = "fixture explicit variant" } }));
        s.InstalledCommunityPromptIds.Add(id);
        var cell = new WorkshopCommunityCell(); var owner = new Window { Width = 440, Height = 350, Content = cell };
        Button Activate() => cell.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ActivatePrompt");
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            // WPF MainWindow.Patreon.cs ActivatePrompt: the explicit-content gate runs before the prompt is applied.
            Activate().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var gate = await Opened<ExplicitContentAcknowledgementDialog>(owner);
            gate.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.False(s.CompanionPrompt.ExplicitContentAcknowledged);
            Activate().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            gate = await Opened<ExplicitContentAcknowledgementDialog>(owner);
            gate.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
            gate.FindControl<Button>("BtnAccept")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            // Accept applies it. (The acknowledgement stamp does not survive: ActivatePrompt replaces
            // CompanionPrompt with the prompt's own settings, exactly as WPF does.)
            Assert.Equal(id, s.ActiveCommunityPromptId);
        }
        finally
        {
            foreach (var w in owner.OwnedWindows.ToList()) w.Close();
            owner.Close(); s.InstalledCommunityPromptIds.Remove(id); s.ActiveCommunityPromptId = null;
            s.SlutModeEnabled = false; s.CompanionPrompt.ExplicitContentAcknowledged = false; File.Delete(file);
        }
    });

    [Fact]
    public Task ImportAndExportButtonsUsePickerPathsAndRefreshReturnsFromOfflineCache() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; s.InstalledCommunityPromptIds.Clear(); s.ActiveCommunityPromptId = null; s.OfflineMode = true;
        var source = Path.Combine(TestUserDataProfile.Root, "Imported Test Persona.json");
        var output = Path.Combine(TestUserDataProfile.Root, "exported-test-persona.json");
        File.WriteAllText(source, JsonConvert.SerializeObject(new CommunityPrompt { Id = "source", Name = "Original", Author = "Test" }));
        var importPick = new TaskCompletionSource<string?>();
        var cell = new WorkshopCommunityCell { ImportFileForTests = () => importPick.Task, ExportFileForTests = () => Task.FromResult<string?>(output) };
        var owner = new Window { Width = 440, Height = 350, Content = cell };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            cell.FindControl<Button>("BtnImportPrompt")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // The picker answers only once Dialog is already waiting, so this dialog always opens late - the
            // path the export's awaited file write takes on Windows - and every OS covers the Opened signal.
            var imported = Dialog(owner); importPick.SetResult(source);
            (await imported).Close(true);
            Assert.Single(s.InstalledCommunityPromptIds);
            var id = s.InstalledCommunityPromptIds.Single();
            Assert.Equal("Imported Test Persona", cell.Service.GetInstalledPrompt(id)!.Name);
            s.CompanionPrompt.Personality = "fixture export text";
            cell.FindControl<Button>("BtnExportPrompt")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await Dialog(owner)).Close(true);
            Assert.Equal("fixture export text", JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(output))!.PromptSettings.Personality);
            var refresh = cell.FindControl<Button>("BtnRefreshPrompts")!;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(refresh.IsEnabled); Assert.Equal(Loc.Get("btn_refresh"), Assert.IsType<TextBlock>(refresh.Content).Text);
            cell.Service.RemovePrompt(id);
        }
        finally { owner.Close(); File.Delete(source); File.Delete(output); }
    });

    [Fact]
    public Task OfflineBrowseShowsTheExistingEmptyCatalogDialog() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.OfflineMode = true;
        var cache = Path.Combine(CorePaths.UserData, "community-prompts", "manifest-cache.json");
        if (File.Exists(cache)) File.Delete(cache);
        var cell = new WorkshopCommunityCell(); var owner = new Window { Width = 440, Height = 350, Content = cell };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            cell.FindControl<Button>("BtnBrowsePrompts")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = await Dialog(owner);
            Assert.Equal(Loc.Get("msg_no_community_prompts"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            dialog.Close(true);
        }
        finally { owner.Close(); }
    });

    private sealed class Catalog : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body = request.RequestUri!.AbsolutePath.EndsWith("manifest")
                ? new CommunityPromptsManifest { Prompts = new[] { new CommunityPromptManifestEntry { Id = "browse-fixture", Name = "Browse fixture", Author = "Test" } } }
                : new CommunityPrompt { Id = "browse-fixture", Name = "Browse fixture", Author = "Test" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(body)) });
        }
    }

    [Fact]
    public Task BrowseCancelDoesNotInstallAndAcceptInstallsTheFirstChoice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.OfflineMode = false; CoreSettings.Current.InstalledCommunityPromptIds.Clear();
        using var http = new HttpClient(new Catalog());
        var service = new CommunityPromptService(http);
        var cell = new WorkshopCommunityCell(service); var owner = new Window { Width = 440, Height = 350, Content = cell };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            var browse = cell.FindControl<Button>("BtnBrowsePrompts")!;
            browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var ask = await Dialog(owner);
            Assert.Contains("Browse fixture", ask.FindControl<TextBlock>("TxtMessage")!.Text);
            ask.Close(false); Dispatcher.UIThread.RunJobs(); Assert.Empty(service.InstalledPromptIds);
            browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); (await Dialog(owner)).Close(true);
            (await Dialog(owner)).Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "browse-fixture" }, service.InstalledPromptIds);
            Assert.Contains(cell.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Browse fixture");
        }
        finally { service.RemovePrompt("browse-fixture"); owner.Close(); }
    });

    [Fact]
    public Task CommunityCellIsReachableThroughTheCompanionPersonalityPage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.InstalledCommunityPromptIds.Clear(); s.ActiveCommunityPromptId = null; s.SlutModeEnabled = false;
        var id = "room-" + Guid.NewGuid().ToString("N");
        var file = Path.Combine(CorePaths.UserData, "community-prompts", id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonConvert.SerializeObject(new CommunityPrompt { Id = id, Name = "Room persona", Author = "Test" }));
        s.InstalledCommunityPromptIds.Add(id);
        var shell = new MainShellWindow();
        try
        {
            // Companion v2 (WPF CompanionTabView.xaml.cs:36-40) collapses the room; the chat's Settings button
            // opens Companion > Personality, which hosts the Community cell (WPF PersonalityPage.xaml.cs:47).
            shell.Show(); shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();
            shell.Named<CompanionTabView>("CompanionTab")!.FindControl<ConversationPage>("Conversation")!
                .FindControl<Button>("SettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("personality", shell.CurrentTab);
            var cell = shell.Named<PersonalityPage>("CompanionPersonalityTab")!.GetLogicalDescendants().OfType<WorkshopCommunityCell>().Single();
            Assert.True(cell.IsEffectivelyVisible);
            Assert.True(cell.IsVisible); Assert.True(cell.FindControl<Button>("BtnBrowsePrompts")!.IsVisible);

            // WPF re-reads Z4 straight after Activate/Remove (MainWindow.CompanionTab.cs:184), not on the next tab show.
            var z4 = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single().ViewModel;
            var active = Loc.GetF("companion_personality_active_custom_fmt", id);
            Assert.NotEqual(active, z4.ActivePersonalityLine);
            cell.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ActivatePrompt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(active, z4.ActivePersonalityLine);
            // WPF UpdateCommunityPromptsUI after a Z4 deactivate (MainWindow.Patreon.cs:2249) and on every tab show
            // (MainWindow.CompanionTab.cs:147): the row's Activate button follows the setting, not the attach.
            bool CanActivate() => cell.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "ActivatePrompt");
            Assert.False(CanActivate());
            z4.ResetPersonalityCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.Null(s.ActiveCommunityPromptId);
            Assert.True(CanActivate());
            cell.Service.ActivatePrompt(id);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();
            shell.ShowTab("personality"); Dispatcher.UIThread.RunJobs();
            Assert.False(CanActivate());
            cell.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemovePrompt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotEqual(active, z4.ActivePersonalityLine);
        }
        finally { shell.Close(); s.InstalledCommunityPromptIds.Remove(id); s.ActiveCommunityPromptId = null; File.Delete(file); }
        return Task.CompletedTask;
    });
}
