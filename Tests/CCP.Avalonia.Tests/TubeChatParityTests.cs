using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Tube parity for a brain-routed send (WPF ChatInput.cs:747 SendChatMessageAsync): the thinking bubble
/// animates while the (loopback fake, protocol 2) model is held, the reply stops it and double-bounces
/// the avatar, and an activity request is followed by an ask card whose chip, clicked, performs it.
/// </summary>
public sealed class TubeChatParityTests
{
    [Fact]
    public Task ThinkingBounceAndAskCardFollowABrainReply() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousSettings = CoreSettings.ServiceProvider;
        var previousAccount = CoreAccount.UnifiedUserId;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.UseCompanionBrain = true;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.CompanionAsksEnabled = true;

        // HttpListener decodes the chunked body AiService sends (JsonContent has no Content-Length).
        // The probed port can be taken between probe.Stop and Start on a busy runner: retry on a fresh one.
        HttpListener listener;
        string baseUrl;
        for (var attempt = 1; ; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
            probe.Stop();
            listener = new HttpListener();
            listener.Prefixes.Add(baseUrl + "/");
            try { listener.Start(); break; }
            catch (HttpListenerException) when (attempt < 10) { listener.Close(); }
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            string body;
            using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = await r.ReadToEndAsync();
            received.SetResult();
            await gate.Task;   // the request has arrived; hold the reply while checking thinking
            var id = System.Text.RegularExpressions.Regex.Match(body, "\"request_id\":\"([^\"]+)\"").Groups[1].Value;
            var json = Encoding.UTF8.GetBytes(
                "{\"companion_protocol\":2,\"finish_reason\":\"stop\",\"request_id\":\"" + id + "\",\"content\":\"hi sweetie\"}");
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = json.Length;
            await ctx.Response.OutputStream.WriteAsync(json);
            ctx.Response.Close();
        });

        var dir = Directory.CreateTempSubdirectory("ccp-tube-").FullName;
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        AvApp.Ai = new AiService(baseUrl);
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        CoreAccount.UnifiedUserId = "u-tube";
        var tube = new AvatarTubeWindow(null);
        var now = TimeSpan.Zero;
        tube.ChatClock = () => now;
        var started = new List<string>();
        var restore = SaveSeams();
        try
        {
            AvApp.SeedCompanionTubeSeams();
            // No games on offer: the parity trunk's launcher opens games, so the activity list would
            // carry them and the Game card would win over the Session fallback this test pins.
            CompanionBrain.ActivitiesProvider = () => Array.Empty<CompanionActivity>();
            CoreDispatch.PostProvider = a => Dispatcher.UIThread.Post(a);
            Assert.NotNull(PromptAssembler.NoticeSurface?.Invoke());       // the oversize toast has a surface
            CompanionAskService.RequestDelay = TimeSpan.Zero;
            CompanionAskService.ShowCardSurface = c => Dispatcher.UIThread.Post(() => tube.ShowAskCard(c));
            CompanionAskService.SessionOptions = () => new[] { new AskOption("s-one", "Session one", () => true) };
            CompanionAskService.StartSession = id => { started.Add(id); return true; };
            Assert.True(ConversationDelivery.AskCardsShown?.Invoke("what can I do"));   // a Session card can follow

            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var speech = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.FindControl<TextBox>("TxtUserInput")!.Text = "what can I do";
            var send = tube.SendChatAsync();

            // In flight: an unbadged thinking phrase that grows dots, and barks are held.
            await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var first = speech.Text;
            Assert.False(string.IsNullOrEmpty(first));
            Assert.True(tube.IsCompanionBusy(0));
            Assert.False(tube.FindControl<Border>("AiBadge")!.IsVisible);
            var thinkingFrame = Assert.IsType<Action>(tube.ThinkingFrame);
            thinkingFrame();
            Assert.NotEqual(first, speech.Text);

            gate.SetResult();
            await send.WaitAsync(TimeSpan.FromSeconds(10));
            await server.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(tube.ChatHistory, m => !m.IsUser && m.Text == "hi sweetie");
            var host = tube.FindControl<Grid>("AvatarBounceHost")!;
            Assert.NotNull(tube.BounceFrame);   // the reply really started the animation
            foreach (var (ms, y) in new[] { (80, -15d), (160, 0d), (220, -8d) })
            {
                now = TimeSpan.FromMilliseconds(ms);
                tube.BounceFrame!();
                Assert.Equal(y, Assert.IsType<TranslateTransform>(host.RenderTransform).Y);
            }
            now = TimeSpan.FromMilliseconds(280);
            tube.BounceFrame!();
            Assert.Null(host.RenderTransform);   // released afterwards, as WPF's FillBehavior.Stop

            // The ask card follows: the question takes the bubble with its chips under it (Game has
            // no games here, so WPF's fallback to a Session card).
            var buttons = tube.FindControl<WrapPanel>("AskButtons")!;
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
                while (!buttons.IsVisible) { await Task.Delay(10, timeout.Token); Dispatcher.UIThread.RunJobs(); }
            Assert.True(buttons.IsVisible);
            Assert.Equal(2, buttons.Children.Count);
            var question = speech.Text;
            Assert.NotEqual("hi sweetie", question);
            Assert.Null(tube.ThinkingFrame);
            thinkingFrame();
            Assert.Equal(question, speech.Text);   // the thinking ticker is dead

            var chip = (Border)buttons.Children[0];
            tube.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var at = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), tube)!.Value;
            tube.MouseDown(at, MouseButton.Left);
            tube.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "s-one" }, started);
        }
        finally
        {
            gate.TrySetResult();
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            try
            {
                listener.Close();
                try { await server.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            }
            finally
            {
                // A fixture shutdown failure must not leave singleton bindings in the next test.
                restore();
                AvApp.Brain?.Dispose();
                AvApp.Ai?.Dispose();
                (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
                CoreAccount.UnifiedUserId = previousAccount;
                CoreSettings.ServiceProvider = previousSettings;
                Directory.Delete(dir, true);
            }
        }
    });

    /// <summary>Every seam SeedCompanionTubeSeams or a test sets, plus the 60 s ask timer it starts.</summary>
    private static Action SaveSeams()
    {
        var saved = (CompanionAskService.BrainProvider, CompanionAskService.ShowCardSurface, CompanionAskService.SaySurface,
            CompanionAskService.BusyProvider, CompanionAskService.SessionOptions, CompanionAskService.StartSession,
            CompanionAskService.OpenLink, CompanionAskService.RequestDelay, ConversationDelivery.AskCardsShown,
            PromptAssembler.NoticeSurface, CoreDispatch.PostProvider);
        var activities = CompanionBrain.ActivitiesProvider;
        return () =>
        {
            CompanionAskService.Instance.Stop();
            (CompanionAskService.BrainProvider, CompanionAskService.ShowCardSurface, CompanionAskService.SaySurface,
             CompanionAskService.BusyProvider, CompanionAskService.SessionOptions, CompanionAskService.StartSession,
             CompanionAskService.OpenLink, CompanionAskService.RequestDelay, ConversationDelivery.AskCardsShown,
             PromptAssembler.NoticeSurface, CoreDispatch.PostProvider) = saved;
            CompanionBrain.ActivitiesProvider = activities;
        };
    }

    private sealed class NoVideoHost : ConditioningControlPanel.Services.IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(ConditioningControlPanel.Services.AttentionVerdict verdict, int ms, Action then) { }
    }

    /// <summary>The model is promised a card only when this head can build one, and never over a lock
    /// card or a mandatory video (WPF CompanionAskService.IsBusy).</summary>
    [Fact]
    public Task NoCardIsPromisedWhenNoneCanFollow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.CompanionAsksEnabled = true;
        var dir = Directory.CreateTempSubdirectory("ccp-ask-").FullName;
        var previousBrain = AvApp.Brain;
        var previousVideo = CoreEngine.Video;
        var previousLockdown = LockdownService.Current;
        AvApp.Brain = new CompanionBrain(new AiService("http://127.0.0.1:9"), memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        var restore = SaveSeams();
        try
        {
            AvApp.SeedCompanionTubeSeams();
            CompanionBrain.ActivitiesProvider = () => Array.Empty<CompanionActivity>();   // no games, as below
            const string ask = "what can I do";
            // No launcher here: no games, no sessions -> nothing to build -> no promise.
            Assert.False(ConversationDelivery.AskCardsShown!(ask));
            // A Watch card follows only with a mod video pool. CoreMods.Service is process-global and stays
            // attached once any test ran App.StartMods (StartModsTests, ModChoiceTests...), so read it.
            var hasVideos = CoreMods.Service?.GetVideoLinks() is { Count: > 0 };
            Assert.Equal(hasVideos, ConversationDelivery.AskCardsShown!("recommend me a video"));

            CompanionAskService.SessionOptions = () => new[] { new AskOption("s-one", "Session one", () => true) };
            Assert.True(ConversationDelivery.AskCardsShown!(ask));

            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false);
            Assert.False(ConversationDelivery.AskCardsShown!(ask));   // busy: a lock card is up
            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            Assert.True(ConversationDelivery.AskCardsShown!(ask));

            var video = new ConditioningControlPanel.Services.MandatoryVideoScheduler(new NoVideoHost(), library: () => new[] { "a.mp4" });
            CoreEngine.Video = video;
            Assert.True(video.Trigger());
            Assert.False(ConversationDelivery.AskCardsShown!(ask));   // busy: a mandatory video is playing
            video.ForceCleanup();
            Assert.True(ConversationDelivery.AskCardsShown!(ask));

            // Audit #1956: WPF's IsBusy counts an active Lockdown (App.xaml.cs BusyProvider).
            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.False(ConversationDelivery.AskCardsShown!(ask));   // busy: lockdown
            ld.Deactivate();
            Assert.True(ConversationDelivery.AskCardsShown!(ask));
        }
        finally
        {
            restore();
            LockdownService.Current?.Deactivate();
            LockdownService.Current = previousLockdown;
            CoreEngine.Video = previousVideo;
            AvApp.Brain?.Dispose();
            AvApp.Brain = previousBrain;
            CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    // WPF App.xaml.cs:779-790: no companion offers while another activity owns the user.
    [Theory]
    [InlineData("quiet")]
    [InlineData("mantra")]
    [InlineData("remote")]
    public void AskCardsWaitForTheUserToBeFree(string owner) => AvaloniaTestDispatcher.Run(() =>
    {
        CompanionPickerHostTests.Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.CompanionAsksEnabled = true;
        CoreTutorial.IsActiveProvider = () => false;
        var relay = ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay;
        using var remote = new ConditioningControlPanel.Services.RemoteRelay(
            () => null, () => null, "test", (_, _) => null, _ => { });
        ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay = new(() => remote);
        var connected = typeof(ConditioningControlPanel.Services.RemoteRelay)
            .GetProperty("ControllerConnected")!;
        var dir = Directory.CreateTempSubdirectory("ccp-ask-owner-").FullName;
        var previousBrain = AvApp.Brain;
        var restore = SaveSeams();
        using var brain = new CompanionBrain(new AiService("http://127.0.0.1:9"),
            memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        AvApp.Brain = brain;
        try
        {
            AvApp.SeedCompanionTubeSeams();
            CompanionBrain.ActivitiesProvider = () => Array.Empty<CompanionActivity>();
            CompanionAskService.SessionOptions = () => new[] { new AskOption("s-one", "Session one", () => true) };
            var asks = new CompanionAskService();
            var shown = 0;
            CompanionAskService.ShowCardSurface = _ => shown++;
            Assert.True(ConversationDelivery.AskCardsShown!("what can I do"));

            if (owner == "quiet") CoreTutorial.IsActiveProvider = () => true;
            if (owner == "mantra") AvApp.Mantra.StartSession(3);
            if (owner == "remote") connected.SetValue(
                ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay.Value, true);
            Assert.False(ConversationDelivery.AskCardsShown!("what can I do"));
            asks.ShowNow(AskKind.Session);
            Assert.Equal(0, shown);

            CoreTutorial.IsActiveProvider = () => false;
            AvApp.Mantra.EndSession();
            connected.SetValue(remote, false);
            Assert.True(ConversationDelivery.AskCardsShown!("what can I do"));
            asks.ShowNow(AskKind.Session);
            Assert.Equal(1, shown);
        }
        finally
        {
            AvApp.Mantra.EndSession();
            restore();
            AvApp.Brain = previousBrain;
            ConditioningControlPanel.Avalonia.Views.Tabs.RemoteControlTabView.Relay = relay;
            brain.Dispose();
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, -7.5)]
    [InlineData(80, -15)]
    [InlineData(160, 0)]
    [InlineData(220, -8)]
    [InlineData(280, 0)]
    public void DoubleBounceKeepsTheWpfKeyframes(double ms, double y) =>
        Assert.Equal(y, AvatarTubeWindow.DoubleBounceY(ms));

    [Fact]
    public void ThinkingPhrasesStripTheirOwnDots()
    {
        Assert.Equal("Poppin bubbles", ThinkingPhrases.StripTrailingDots("*Poppin bubbles...*"));
        Assert.Equal("(thinking)", ThinkingPhrases.StripTrailingDots("(thinking...)"));
        Assert.Equal("*~*", ThinkingPhrases.StripTrailingDots("*~*"));
        Assert.Equal("x..", ThinkingPhrases.Frame("x", 2));
    }
}
