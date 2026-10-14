using System;
using System.IO;
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
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MakeHerYoursCommunityTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var manifest = request.RequestUri!.AbsolutePath.EndsWith("manifest");
            if (!manifest) Downloads++;
            var body = manifest ? JsonConvert.SerializeObject(new CommunityPromptsManifest { Prompts = new[] {
                new CommunityPromptManifestEntry { Id = "new", Name = "New personality", Author = "Author", Description = "About" } } })
                : JsonConvert.SerializeObject(new CommunityPrompt { Id = "new", Name = "New personality" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public Task CommunityLinkBrowsesCancelsInstallsAndReadoutUsesInstalledName() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-community-card-").FullName;
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var catalogue = new CommunityPromptCatalogue(http, dir);
        Window? owner = null;
        try
        {
            var s = service.Current;
            s.OfflineMode = false;
            s.InstalledCommunityPromptIds.Clear();
            s.ActiveCommunityPromptId = "installed-id";
            File.WriteAllText(Path.Combine(dir, "installed-id.json"), JsonConvert.SerializeObject(new CommunityPrompt { Id = "installed-id", Name = "Installed friendly name" }));
            var view = new MakeHerYoursView();
            view.ViewModel.PromptCatalogue = catalogue;
            view.ViewModel.Sync();
            owner = new Window { Width = 500, Height = 700, Content = view };
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "Installed friendly name"), view.ViewModel.ActivePersonalityLine);
            var community = view.FindControl<Button>("BtnCommunityPrompts")!;
            Assert.True(community.IsEnabled);
            MessageDialog Dialog() => (MessageDialog)owner.OwnedWindows[0];
            async Task WaitDialog()
            { for (int i = 0; i < 100 && owner.OwnedWindows.Count == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); } }
            community.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = community.TranslatePoint(new Point(community.Bounds.Width / 2, community.Bounds.Height / 2), owner)!.Value;
            owner.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
            owner.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
            await WaitDialog();
            Assert.Contains("New personality by Author", Dialog().FindControl<TextBlock>("TxtMessage")!.Text);
            Dialog().FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, handler.Downloads);
            var browsing = view.ViewModel.BrowseCommunityPromptsAsync();
            await WaitDialog();
            Dialog().FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await WaitDialog();
            Assert.Equal(Loc.Get("title_installed"), Dialog().Title);
            Dialog().FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await browsing;
            Assert.Equal(1, handler.Downloads);
            Assert.Contains("new", s.InstalledCommunityPromptIds);
            Assert.Equal("installed-id", s.ActiveCommunityPromptId);
            s.ActiveCommunityPromptId = "missing";
            view.ViewModel.Sync();
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "missing"), view.ViewModel.ActivePersonalityLine);
            var allInstalled = view.ViewModel.BrowseCommunityPromptsAsync();
            await WaitDialog();
            Assert.Equal(Loc.Get("msg_all_prompts_installed"), Dialog().FindControl<TextBlock>("TxtMessage")!.Text);
            Dialog().FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await allInstalled;
            using var empty = new CommunityPromptCatalogue(http, Path.Combine(dir, "empty"));
            view.ViewModel.PromptCatalogue = empty;
            s.OfflineMode = true;
            var none = view.ViewModel.BrowseCommunityPromptsAsync();
            await WaitDialog();
            Assert.Equal(Loc.Get("msg_no_community_prompts"), Dialog().FindControl<TextBlock>("TxtMessage")!.Text);
            Dialog().FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await none;
            Assert.Equal(1, handler.Downloads);
        }
        finally { owner?.Close(); service.SaveImmediate(); CoreSettings.ServiceProvider = previous; Directory.Delete(dir, true); }
    });
}
