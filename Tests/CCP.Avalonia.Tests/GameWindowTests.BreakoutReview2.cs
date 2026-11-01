using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Breakout_EarlyReadyWaitsForSuccessfulMatchingNavigation() => Run((_, launcher) =>
    {
        launcher.Hide();
        var w = GameWindow.Launch("breakoutdemo")!;
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        var oldSend = GameWindow.FunnelSend;
        var counts = new List<string>();
        GameWindow.FunnelSend = (_, body) => counts.Add(body);
        try
        {
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"gate_shown\",\"a\":\"breakout_demo\",\"b\":\"prime\"}");
            Assert.Empty(frames);
            Assert.Empty(counts);
            w.Web.OnNavigationCompleted(w.PageUrl!);
            Assert.Equal("gate_shown|panel|breakout_demo|prime", Assert.Single(counts));
        }
        finally { GameWindow.FunnelSend = oldSend; }
        Assert.Equal("init", (string?)Assert.Single(frames)["type"]);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Single(frames);
        w.Close();
        var bad = GameWindow.Launch("breakoutdemo")!;
        var sent = new List<string>();
        bad.Posted += sent.Add;
        bad.Web.OnWebMessage("{\"type\":\"ready\"}");
        bad.Web.OnNavigationCompleted(bad.PageUrl!, success: false);
        bad.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Empty(sent);
        bad.Close();
        var other = GameWindow.Launch("breakoutdemo")!;
        other.Posted += sent.Add;
        other.Web.OnWebMessage("{\"type\":\"ready\"}");
        other.Web.OnNavigationCompleted(new Uri(other.PageUrl!, "/other"));
        Assert.Empty(sent);
    });

    [Fact]
    public void Breakout_MinimizeSuspendsAndDropsQueuedCheckout() => Run((_, launcher) =>
    {
        launcher.Hide();
        var opened = new List<string>();
        var frames = new List<JObject>();
        var oldOpen = GameWindow.FunnelOpen;
        var oldSend = GameWindow.FunnelSend;
        var oldLast = GameWindow.FunnelLastOpenUtc;
        try
        {
            GameWindow.FunnelOpen = opened.Add;
            GameWindow.FunnelSend = (_, _) => { };
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            var w = GameWindow.Launch("breakoutdemo")!;
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl!);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.WindowState = WindowState.Minimized;
            Assert.True((bool)frames[^1]["on"]!);
            w.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"prime\"}");
            Assert.Empty(opened);
            w.WindowState = WindowState.Normal;
            Assert.False((bool)frames[^1]["on"]!);
            w.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"prime\"}");
            Assert.Single(opened);
        }
        finally { (GameWindow.FunnelOpen, GameWindow.FunnelSend, GameWindow.FunnelLastOpenUtc) = (oldOpen, oldSend, oldLast); }
    });

    [Fact]
    public void Breakout_VaultFetchFeedsInitAndCheckout() => Run((_, launcher) =>
    {
        launcher.Hide();
        PlanCatalog.Reset();
        var oldOpen = GameWindow.FunnelOpen;
        var oldSend = GameWindow.FunnelSend;
        var oldLast = GameWindow.FunnelLastOpenUtc;
        try
        {
            PlanCatalog.FetchOverride = _ => Task.FromResult<string?>("""
            {"currency":"USD","basic":{"name":"Basic","monthly":"8.50","monthly_eur":"7.00","yearly":"80.00","patreon_url":"https://www.patreon.com/checkout/CodeBambi?rid=1","substar_url":"https://subscribestar.adult/cc-labs","web_url":"https://app.cclabs.app/subscribe?plan=basic"},"prime":{"name":"Prime","monthly":"14.50","monthly_eur":"12.00","yearly":"140.00","patreon_url":"https://www.patreon.com/checkout/CodeBambi?rid=99","substar_url":"https://subscribestar.adult/cc-labs","web_url":"https://app.cclabs.app/subscribe?plan=prime"}}
            """);
            var vault = new VaultGateDialog();
            vault.FillPlansAsync().GetAwaiter().GetResult();
            Assert.True(PlanCatalog.Current.FromServer);
            var opened = new List<string>();
            GameWindow.FunnelOpen = opened.Add;
            GameWindow.FunnelSend = (_, _) => { };
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            var w = GameWindow.Launch("breakoutdemo")!;
            var init = JObject.FromObject(w.InitMessage());
            Assert.Equal("14.50", (string?)init["plans"]!["prime"]!["monthly"]);
            w.Web.OnNavigationCompleted(w.PageUrl!);
            w.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"prime\"}");
            Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=99", Assert.Single(opened));
        }
        finally { PlanCatalog.Reset(); (GameWindow.FunnelOpen, GameWindow.FunnelSend, GameWindow.FunnelLastOpenUtc) = (oldOpen, oldSend, oldLast); }
    });

    [Fact]
    public void Breakout_CheckoutReturnRefreshesPurchasedTier() => Run((_, launcher) =>
    {
        launcher.Hide();
        var s = CoreSettings.Current;
        var oldV2 = AccountSeed.NewV2;
        var oldSecrets = CoreSecrets.RetrieveProvider;
        var oldOpen = GameWindow.FunnelOpen;
        var oldSend = GameWindow.FunnelSend;
        var oldLast = GameWindow.FunnelLastOpenUtc;
        try
        {
            CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
            s.UnifiedId = "test-account"; s.PatreonTier = 0; s.OfflineMode = false;
            AccountSeed.NewV2 = () => new V2AuthService(() => s, new CheckoutAnswer());
            AccountSeed.CheckoutOpenedUtc = null;
            AccountSeed.CheckoutLastCheckUtc = DateTime.MinValue;
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            GameWindow.FunnelOpen = _ => { };
            GameWindow.FunnelSend = (_, _) => { };
            var w = GameWindow.Launch("breakoutdemo")!;
            w.Web.OnNavigationCompleted(w.PageUrl!);
            w.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"prime\"}");
            Assert.NotNull(AccountSeed.CheckoutOpenedUtc);
            var now = DateTime.UtcNow;
            AccountSeed.CheckoutLastCheckUtc = now;
            AccountSeed.OnCheckoutActivatedAsync(now.AddSeconds(5)).GetAwaiter().GetResult();
            Assert.Equal(0, s.PatreonTier);
            AccountSeed.CheckoutLastCheckUtc = DateTime.MinValue;
            launcher.Show(); launcher.Activate(); Dispatcher.UIThread.RunJobs();
            w.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return s.PatreonTier == 2; }, TimeSpan.FromSeconds(5)),
                $"checkout activation was observed: {AccountSeed.CheckoutLastCheckUtc != DateTime.MinValue}");
            Assert.Equal(2, s.PatreonTier);
            Assert.True(s.HasCachedLabAccess);
            Assert.Null(AccountSeed.CheckoutOpenedUtc);
        }
        finally
        {
            AccountSeed.NewV2 = oldV2;
            CoreSecrets.RetrieveProvider = oldSecrets;
            AccountSeed.CheckoutOpenedUtc = null;
            (GameWindow.FunnelOpen, GameWindow.FunnelSend, GameWindow.FunnelLastOpenUtc) = (oldOpen, oldSend, oldLast);
        }
    });

    private sealed class CheckoutAnswer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"user\":{\"effective_tier\":2}}") });
    }
}
