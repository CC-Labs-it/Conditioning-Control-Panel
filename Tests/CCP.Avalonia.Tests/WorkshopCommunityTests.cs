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
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopCommunityTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(6, false)]
    public Task BrowseMatchesWpfEmptyAllInstalledAndFirstFive(int count, bool installed) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        if (installed) service.Current.InstalledCommunityPromptIds.Add("voice0");
        var handler = new CatalogHandler(count);
        var drawer = new WorkshopAccordion { CommunityCatalogFactory = () => new CommunityPromptCatalog(new HttpClient(handler)) };
        var owner = new Window { Content = drawer, Width = 1160, Height = 700 };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            await drawer.CommunityActionAsync("refresh");
            var browse = drawer.CommunityActionAsync("browse");
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(owner.OwnedWindows);
            var text = dialog.FindControl<TextBlock>("TxtMessage")!.Text!;
            if (count == 6)
            {
                Assert.Contains("Voice 0", text); Assert.Contains("Voice 4", text); Assert.DoesNotContain("Voice 5", text);
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("voice0", service.Current.InstalledCommunityPromptIds);
                Assert.DoesNotContain("voice1", service.Current.InstalledCommunityPromptIds);
                WorkshopAccordion.CloseCommunityForPanic(); await browse;
            }
            else
            {
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get(installed ? "msg_all_prompts_installed" : "msg_no_community_prompts"), text);
                WorkshopAccordion.CloseCommunityForPanic(); await browse;
                Assert.Equal(installed ? 1 : 0, service.Current.InstalledCommunityPromptIds.Count);
            }
        }
        finally
        {
            WorkshopAccordion.CloseCommunityForPanic(); owner.Close();
            foreach (var id in service.Current.InstalledCommunityPromptIds.ToArray()) InstalledCommunityPrompts.Remove(id);
            service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
    });
    private sealed class CatalogHandler(int count) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("manifest")
                ? Newtonsoft.Json.JsonConvert.SerializeObject(new { Prompts = Enumerable.Range(0, count).Select(i => new { Id = "voice" + i, Name = "Voice " + i, Author = "Anonymous" }) })
                : "{\"Id\":\"voice0\",\"Name\":\"Voice 0\",\"PromptSettings\":{}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Fact]
    public Task ImportExportCancellationInstalledRowsAndPanicUseTheRealDrawer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var file = Path.Combine(Path.GetTempPath(), "ccp-community-" + Guid.NewGuid().ToString("N") + ".json");
        var drawer = new WorkshopAccordion();
        var parts = ((WorkshopRuntimeVm)drawer.DataContext!).Parts;
        drawer.ViewModel!.IsExpanded = true;
        var owner = new Window { Content = drawer, Width = 1160, Height = 700 };
        try
        {
            owner.Show(); Dispatcher.UIThread.RunJobs();
            File.WriteAllText(file, "{\"Name\":\"ignored\",\"PromptSettings\":{\"Personality\":\"kind\"}}");
            drawer.CommunityPicker = (_, _) => Task.FromResult<string?>(file);
            parts.Community.FindControl<Button>("BtnImportPrompt")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(service.Current.InstalledCommunityPromptIds);
            Assert.False(parts.Community.FindControl<TextBlock>("TxtNoInstalledPrompts")!.IsVisible);
            Assert.Single(owner.OwnedWindows);
            PanicSurfaces.All.Single(s => s.Id == "workshop-community").Stop(null);
            Dispatcher.UIThread.RunJobs(); Assert.Empty(owner.OwnedWindows);
            drawer.CommunityPicker = (_, _) => Task.FromResult<string?>(null);
            await drawer.CommunityActionAsync("import"); Assert.Single(service.Current.InstalledCommunityPromptIds);
            drawer.CommunityPicker = (_, _) => Task.FromResult<string?>(file);
            var export = drawer.CommunityActionAsync("export");
            // File IO yields; pump until the success modal is presented.
            for (int i = 0; i < 100 && !owner.OwnedWindows.Any(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Contains("My Custom Personality", File.ReadAllText(file));
            Assert.Single(owner.OwnedWindows); WorkshopAccordion.CloseCommunityForPanic(); await export;
            InstalledCommunityPrompts.Remove(service.Current.InstalledCommunityPromptIds.Single());
            drawer.RefreshInstalledPrompts(); Assert.True(parts.Community.FindControl<TextBlock>("TxtNoInstalledPrompts")!.IsVisible);
        }
        finally
        {
            WorkshopAccordion.CloseCommunityForPanic(); owner.Close(); File.Delete(file);
            foreach (var id in service.Current.InstalledCommunityPromptIds.ToArray()) InstalledCommunityPrompts.Remove(id);
            service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
    });
}
