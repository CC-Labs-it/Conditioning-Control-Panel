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
    private static async Task<MessageDialog> Dialog(Window owner)
    {
        for (int i = 0; i < 500; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (owner.OwnedWindows.OfType<MessageDialog>().FirstOrDefault() is { } dialog) return dialog;
            await Task.Delay(10);
        }
        throw new InvalidOperationException("Expected a message dialog");
    }

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
        var cell = new WorkshopCommunityCell { ImportFileForTests = () => Task.FromResult<string?>(source), ExportFileForTests = () => Task.FromResult<string?>(output) };
        var owner = new Window { Width = 440, Height = 350, Content = cell };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            cell.FindControl<Button>("BtnImportPrompt")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await Dialog(owner)).Close(true);
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
    public Task CommunityCellIsReachableThroughTheCompanionWorkshopDrawer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();
            shell.GetLogicalDescendants().OfType<WorkshopAccordion>().Single().ExpandAndReveal(); Dispatcher.UIThread.RunJobs();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopCommunityCell>().Single();
            Assert.True(cell.IsVisible); Assert.True(cell.FindControl<Button>("BtnBrowsePrompts")!.IsVisible);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
