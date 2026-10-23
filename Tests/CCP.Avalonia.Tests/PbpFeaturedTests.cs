using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PbpFeaturedTests
{
    private const string Match = "m_0123456789abcdef";
    private static void Setup()
    {
        if (Application.Current is null)
        {
            // CI has no Windows fonts: deterministic bundled faces for this behavior test.
            var fonts = AppFonts.Options(systemEmoji: false);
            var mappings = new Dictionary<string, global::Avalonia.Media.FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(fonts)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
    }

    [Fact]
    public Task BoardPollVisibilityProviderAndRelease() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.DashboardBrowserCollapsed = true;
        var old = PbpFeaturedFeed.Fetch;
        PbpFeaturedFeed.ResetForTests();
        int asks = 0;
        PbpFeaturedFeed.Fetch = _ => { asks++; return Task.FromResult((200, (string?)
            "{\"match\":{\"id\":\"" + Match + "\",\"white\":\"Ann\",\"black\":\"Bo\",\"moves\":12,\"watchers\":3}}")); };
        var shell = new MainShellWindow();
        try
        {
            Assert.Equal(0, asks);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, asks);
            Assert.True(PbpFeaturedFeed.Active);
            shell.BillboardHost!.Begin();
            Assert.Equal(FeaturedCards.CardId, shell.BillboardHost.CurrentCard!.Spec.Id);
            if (Environment.GetEnvironmentVariable("CCP_FEATURED_PROOF_DIR") is { Length: > 0 } proof)
            {
                System.IO.Directory.CreateDirectory(proof);
                Dispatcher.UIThread.RunJobs();
                using var frame = shell.CaptureRenderedFrame()!;
                frame.Save(System.IO.Path.Combine(proof, "featured-tonight-board.png"));
            }
            shell.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            Assert.False(PbpFeaturedFeed.Active);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.True(PbpFeaturedFeed.Active);
            string? watched = null;
            var provider = new FeaturedProvider(id => watched = id);
            var card = Assert.Single(provider.Current(new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now)));
            Assert.Equal("Ann vs Bo", card.Title);
            provider.Invoke(card.Action.Target);
            Assert.Equal(Match, watched);
            // The real path: the deck card's button -> RunBillboardAction -> provider -> WatchFeatured (signed out: login).
            Assert.Equal(FeaturedCards.CardId, shell.BillboardHost.CurrentCard!.Spec.Id);
            shell.BillboardHost.Cta.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close(false);
            shell.WindowState = WindowState.Minimized;
            Assert.False(PbpFeaturedFeed.Active);
            shell.WindowState = WindowState.Normal;
            Assert.True(PbpFeaturedFeed.Active);
            Assert.Equal(1, asks); // a quick return does not ask early
            // The match ends: Changed -> MarkDirty, and the next card change drops the featured card.
            PbpFeaturedFeed.Fetch = _ => { asks++; return Task.FromResult((200, (string?)"{\"match\":null}")); };
            typeof(PbpFeaturedFeed).GetField("_lastAskUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, DateTime.MinValue);
            shell.WindowState = WindowState.Minimized;
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, asks);
            Assert.True(shell.BillboardHost.Deck.IsDirty);
            shell.BillboardHost.Advance();
            Assert.DoesNotContain(shell.BillboardHost.Deck.Cards, c => c.Spec.Id == FeaturedCards.CardId);
            shell.Close();
            Assert.False(PbpFeaturedFeed.Active);
            provider.Detach();
            await Task.CompletedTask;
        }
        finally { shell.Close(); PbpFeaturedFeed.ResetForTests(); PbpFeaturedFeed.Fetch = old; }
    });

    [Fact]
    public void WatchInitialThenReadyIntentAndPanic() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var root = System.IO.Directory.CreateTempSubdirectory("featured-game-").FullName;
        using var server = new WebAssetServer(root);
        var old = GameWindow.Server;
        var oldHttp = GameWindow.PbpHttpClient;
        var oldBase = GameWindow.PbpServerBase;
        var oldSecrets = CoreSecrets.RetrieveProvider;
        var transport = new WatchTransport();
        using var http = new System.Net.Http.HttpClient(transport);
        GameWindow.PbpHttpClient = () => http;
        GameWindow.PbpServerBase = () => "http://pbp.test";
        CoreSecrets.RetrieveProvider = _ => "test-token";
        GameWindow.Server = () => server;
        try
        {
            Assert.Equal("test-token", CoreSettings.Current.AuthToken);
            Assert.Null(GameWindow.HostSpectate("bad"));
            var board = GameWindow.HostSpectate(Match)!;
            var sent = new List<JObject>();
            board.Posted += json => sent.Add(JObject.Parse(json));
            board.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal(Match, (string?)sent.Single(o => (string?)o["type"] == "pbp:settings")["spectateMatchId"]);
            Assert.DoesNotContain(sent, o => (string?)o["type"] == "pbp:friend");
            Assert.Same(board, GameWindow.HostSpectate(Match));
            Assert.Equal("spectate", (string?)sent.Last()["mode"]);
            Assert.Equal(Match, (string?)sent.Last()["matchId"]);
            board.HandleMessage("{\"type\":\"pbp:net\",\"id\":\"watch\",\"method\":\"GET\",\"path\":\"/v2/pbp/watch/" + Match + "/chunk/1\"}");
            Assert.True(board.PbpPending.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, transport.Calls);
            Assert.False(transport.CarriedToken);
            var shell = new MainShellWindow();
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Assert.False(GameWindow.IsAnyOpen());
            shell.Close();
        }
        finally
        {
            GameWindow.Server = old;
            GameWindow.PbpHttpClient = oldHttp;
            GameWindow.PbpServerBase = oldBase;
            CoreSecrets.RetrieveProvider = oldSecrets;
            GameWindow.CloseAllForPanic();
            System.IO.Directory.Delete(root, true);
        }
    });

    private sealed class WatchTransport : System.Net.Http.HttpMessageHandler
    {
        internal bool CarriedToken;
        internal int Calls;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken token)
        {
            Calls++;
            CarriedToken = request.Headers.Contains("X-Auth-Token");
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent("{}") });
        }
    }
}
