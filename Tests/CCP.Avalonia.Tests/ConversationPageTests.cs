using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF V2/ConversationPage + AiPage name/recap hosts, driven from the shell's Companion tab.</summary>
public sealed class ConversationPageTests
{
    [Fact]
    public System.Threading.Tasks.Task PortraitHasWpfDustLayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var page = new ConversationPage();
        var dust = page.FindControl<ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas>("StageDust");
        Assert.NotNull(dust);
        Assert.Equal(ConditioningControlPanel.Avalonia.Controls.AmbientFxLayers.DustField, dust.Layers);
        Assert.False(dust.IsHitTestVisible);
        Assert.False(dust.IsRunning); // unattached surfaces never tick
        using var send = new System.Threading.CancellationTokenSource();
        typeof(ConversationPageVm).GetField("_send", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(page.Vm, send);
        page.StopForPanic();
        Assert.True(send.IsCancellationRequested);
        Assert.False(dust.IsRunning);
        return System.Threading.Tasks.Task.CompletedTask;
    });

    [Fact]
    public System.Threading.Tasks.Task CompanionTabSendsShowsAndFollowsTheAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.AwarenessModeEnabled) = (true, true, false);
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52
        (s.AiChatEnabled, s.OfflineMode, s.UseCompanionBrain) = (true, false, true);
        (s.CompanionPrompt.AiProvider, s.CompanionPrompt.ChatMemoryEnabled) = (AiProviderType.Cloud, true);

        // A loopback cloud that answers every chat with "hi sweetie" (companion protocol 2 echoes the id).
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                using (var r = new StreamReader(ctx.Request.InputStream))
                {
                    var id = System.Text.RegularExpressions.Regex.Match(r.ReadToEnd(), "\"request_id\":\"([^\"]+)\"").Groups[1].Value;
                    var json = Encoding.UTF8.GetBytes("{\"companion_protocol\":2,\"finish_reason\":\"stop\",\"request_id\":\"" + id + "\",\"content\":\"hi sweetie\"}");
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(json);
                }
                ctx.Response.Close();
            }
        });

        var dir = Directory.CreateTempSubdirectory("ccp-conv-").FullName;
        var (previousAi, previousBrain, previousUser) = (AvApp.Ai, AvApp.Brain, CoreAccount.UnifiedUserId);
        CoreAccount.UnifiedUserId = "conv-A";
        var aPath = MemoryStore.PreviewAccountDirectory("conv-A", dir);
        new CompanionSessionStore(Path.Combine(aPath, "session.json"), Path.Combine(aPath, "no-import.json")).Save(new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hello there"),
            CompanionTurn.Create(TurnKind.BarkEcho, "a bark is not a chat line"),
            CompanionTurn.Create(TurnKind.AssistantChat, "hi, cutie")
        });
        var ai = new AiService(baseUrl);
        var brain = new CompanionBrain(ai, preview: () => true, accountDirectory: dir);
        (AvApp.Ai, AvApp.Brain) = (ai, brain);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var page = shell.GetLogicalDescendants().OfType<ConversationPage>().Single();

            var dust = page.FindControl<ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas>("StageDust")!;
            Assert.True(dust.IsRunning);
            page.Park();
            Assert.False(dust.IsRunning);
            page.Resume();
            Assert.True(dust.IsRunning);
            using (var pending = new System.Threading.CancellationTokenSource())
            {
                var sendField = typeof(ConversationPageVm).GetField("_send", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                sendField.SetValue(page.Vm, pending);
                try
                {
                    PanicSurfaces.All.Single(s => s.Id == "companion-conversation").Stop(shell);
                    Assert.True(pending.IsCancellationRequested);
                    Assert.False(dust.IsRunning);
                }
                finally { sendField.SetValue(page.Vm, null); }
            }

            // The page is a projection of the brain's one conversation: chat turns only, in order.
            Assert.Equal(new[] { "hello there", "hi, cutie" }, page.Vm.Turns.Select(t => t.Text));
            Assert.True(page.Vm.Turns[0].IsUser);

            // Typing and clicking Send goes through the brain; the exchange lands in the transcript.
            page.FindControl<TextBox>("Composer")!.Text = "ping";
            Dispatcher.UIThread.RunJobs();
            page.FindControl<Button>("SendButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until = System.DateTime.UtcNow.AddSeconds(10);   // P73: bounded by time
            while ((page.Vm.Busy || page.Vm.Turns.Count < 4) && System.DateTime.UtcNow < until)
            {
                await System.Threading.Tasks.Task.Yield();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(new[] { "hello there", "hi, cutie", "ping", "hi sweetie" }, page.Vm.Turns.Select(t => t.Text));
            Assert.Equal(("", ""), (page.Vm.Draft, page.Vm.Notice));

            // The bubble style must not reach the Borders in the inner lists' templates (16px gap per line).
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var transcript = page.FindControl<ItemsControl>("Messages")!;
            var inner = transcript.GetVisualDescendants().OfType<Border>().Where(b => !b.Classes.Contains("line")).ToList();
            Assert.NotEmpty(inner);
            Assert.All(inner, b => Assert.NotEqual(new Thickness(0, 0, 44, 16), b.Margin));
            if (System.Environment.GetEnvironmentVariable("CCP_CONVERSATION_RENDER") is { Length: > 0 } png)
                shell.CaptureRenderedFrame()?.Save(png);

            // An account switch (CoreAccount.UnifiedIdentityChanged) drops the other account's thread and draft.
            page.Vm.Draft = "half typed";
            CoreAccount.UnifiedUserId = "conv-B";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(page.Vm.Turns);
            Assert.Equal("", page.Vm.Draft);

            // Settings / connection / video links open the Companion pages, as WPF OpenSheet does.
            Assert.Equal(("personality", "companionlinks", "companionai"),
                (ConversationPage.PageFor("who"), ConversationPage.PageFor("videos"), ConversationPage.PageFor("connection")));

            // Companion > AI carries the preferred-name editor above the diary; clicking Save writes the profile.
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            var editor = shell.GetLogicalDescendants().OfType<AiPage>().Single()
                .GetLogicalDescendants().OfType<PreferredNameEditor>().Single();
            editor.NameBox.Text = "  Kim   Rose ";
            editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Kim Rose", brain.Memory.Profile[MemoryStore.KeyPreferredName]);
            Assert.Equal("Kim Rose", editor.NameBox.Text);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            listener.Stop();
            (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
            CoreAccount.UnifiedUserId = previousUser;
            brain.Dispose();
            ai.Dispose();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    });
}
