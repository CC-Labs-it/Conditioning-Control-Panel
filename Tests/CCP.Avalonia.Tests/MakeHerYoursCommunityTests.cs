using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Z4's Community link (WPF BtnBrowsePrompts_Click, MainWindow.Patreon.cs:2252-2291) driven
/// from the shell's Companion > Personality page, through the shared Core CommunityPromptService.</summary>
public sealed class MakeHerYoursCommunityTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var manifest = request.RequestUri!.AbsolutePath.EndsWith("manifest");
            if (!manifest) Downloads++;
            object body = manifest
                ? new CommunityPromptsManifest { Prompts = new[] { new CommunityPromptManifestEntry { Id = "new", Name = "New personality", Author = "Author", Description = "About" } } }
                : new CommunityPrompt { Id = "new", Name = "New personality", Author = "Author" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(body)) });
        }
    }

    [Fact]
    public Task CommunityLinkFromTheShellBrowsesCancelsInstallsAndReadoutUsesInstalledName() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.OfflineMode = false;
        s.InstalledCommunityPromptIds.Clear();
        s.ActiveCommunityPromptId = null;
        var dir = Path.Combine(TestUserDataProfile.Root, "mhy-community-" + Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var prompts = new CommunityPromptService(http, dir);
        using var emptyPrompts = new CommunityPromptService(http, Path.Combine(dir, "empty"));

        // Event-driven, time-bounded wait for each MessageDialog (no polling, no sleeps).
        var opened = new Queue<MessageDialog>();
        TaskCompletionSource? next = null;
        using var hook = Window.WindowOpenedEvent.AddClassHandler<MessageDialog>((d, _) => { opened.Enqueue(d); next?.TrySetResult(); });
        async Task<MessageDialog> NextDialog()
        {
            if (opened.Count == 0)
            {
                next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await next.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return opened.Dequeue();
        }
        static string Text(MessageDialog d) => d.FindControl<TextBlock>("TxtMessage")!.Text!;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            // Companion v2 (WPF CompanionTabView.xaml.cs:36-40): Z4 and the Community cell live on
            // Companion > Personality (WPF PersonalityPage.xaml.cs:38-48), adopted out of the collapsed room.
            shell.ShowTab("personality");
            Dispatcher.UIThread.RunJobs();
            var view = shell.GetLogicalDescendants().OfType<MakeHerYoursView>().Single();
            Assert.NotNull(view.FindAncestorOfType<global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages.PersonalityPage>());
            // The one shared service is the room's Workshop COMMUNITY cell's (WPF App.CommunityPrompts).
            var cell = (shell.RoomView()!.WorkshopZone.DataContext as WorkshopRuntimeVm)!.Parts.Community;
            cell.Service = prompts;
            bool CellLists(string name) => cell.FindControl<StackPanel>("InstalledPromptsPanel")!.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == name);
            File.WriteAllText(Path.Combine(dir, "community-prompts", "installed-id.json"),
                JsonConvert.SerializeObject(new CommunityPrompt { Id = "installed-id", Name = "Installed friendly name" }));
            s.ActiveCommunityPromptId = "installed-id";
            view.ViewModel.Sync();
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "Installed friendly name"), view.ViewModel.ActivePersonalityLine);

            var link = view.FindControl<Button>("BtnCommunityPrompts")!;
            Assert.True(link.IsEnabled);
            void ClickLink()
            {
                link.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var at = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }

            // Cancel: the first-five list is shown, nothing is downloaded.
            ClickLink();
            var ask = await NextDialog();
            Assert.Equal(Loc.Get("title_browse_community_prompts"), ask.Title);
            Assert.Contains("New personality by Author", Text(ask));
            ask.Close(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, handler.Downloads);

            // Install: first candidate installed, NOT activated (WPF), installed dialog names it.
            ClickLink();
            (await NextDialog()).Close(true);
            var done = await NextDialog();
            Assert.Equal(Loc.Get("title_installed"), done.Title);
            Assert.Equal(Loc.GetF("msg_prompt_installed", "New personality"), Text(done));
            done.Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, handler.Downloads);
            Assert.Contains("new", s.InstalledCommunityPromptIds);
            Assert.Equal("installed-id", s.ActiveCommunityPromptId);
            Assert.True(CellLists("New personality"));   // WPF UpdateCommunityPromptsUI: the Workshop rows follow a Z4 install

            // Everything installed.
            ClickLink();
            var all = await NextDialog();
            Assert.Equal(Loc.Get("msg_all_prompts_installed"), Text(all));
            all.Close(true);
            Dispatcher.UIThread.RunJobs();

            // Offline with no cache: no prompts.
            cell.Service = emptyPrompts;
            s.OfflineMode = true;
            ClickLink();
            var none = await NextDialog();
            Assert.Equal(Loc.Get("msg_no_community_prompts"), Text(none));
            none.Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, handler.Downloads);

            // Missing file: the id stands in, as WPF.
            s.ActiveCommunityPromptId = "missing";
            view.ViewModel.Sync();
            Assert.Equal(Loc.GetF("companion_personality_active_custom_fmt", "missing"), view.ViewModel.ActivePersonalityLine);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            s.ActiveCommunityPromptId = null;
            s.InstalledCommunityPromptIds.Clear();
            CoreSettings.ServiceProvider = previous;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    });
}
