using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DtrhFunnelTests
{
    private static JObject Message(string evt, string? plan = null, string rail = "patreon") => new()
    {
        ["type"] = "funnel", ["event"] = evt, ["a"] = "Dtrh", ["b"] = rail,
        ["plan"] = plan, ["url"] = "https://untrusted.invalid/checkout",
    };

    [Fact]
    public async Task CountsBlockedAndThrottledClicksAndOpensOnlyCatalogUrls()
    {
        FunnelPageMessage.ResetForTests();
        bool offline = CoreSettings.Current.OfflineMode;
        var sent = new ConcurrentQueue<(string Url, string Body)>();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new List<string>();
        int count = 0;
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            CoreSettings.Current.OfflineMode = false;
            FunnelPageMessage.BaseUrlOverride = "http://127.0.0.1:9";
            FunnelPageMessage.SendOverride = (url, body) =>
            {
                sent.Enqueue((url, body));
                if (Interlocked.Increment(ref count) == 7) finished.TrySetResult(true);
                return Task.CompletedTask;
            };
            FunnelPageMessage.OpenCheckout = url => { opened.Add(url); return true; };
            FunnelPageMessage.UtcNow = () => now;
            FunnelPageMessage.Handle(Message("gate_shown", "basic", "Basic"), true);
            var cta = Message("cta_clicked", "basic");
            FunnelPageMessage.Handle(cta, false); // a run or exit still counts, but cannot open
            Assert.Empty(opened);
            FunnelPageMessage.Handle(cta, true);
            FunnelPageMessage.Handle(cta, true);
            now = now.AddMilliseconds(2999);
            FunnelPageMessage.Handle(cta, true);
            Assert.Single(opened);
            now = now.AddMilliseconds(1);
            FunnelPageMessage.Handle(cta, true);
            Assert.Equal(new[] { PlanCatalog.Current.Basic.PatreonUrl, PlanCatalog.Current.Basic.PatreonUrl }, opened);
            FunnelPageMessage.Handle(Message("cta_clicked", "unknown"), true);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, sent.Count);
            Assert.All(sent, x => Assert.Equal("http://127.0.0.1:9/v2/public/funnel", x.Url));
            Assert.Contains(sent, x => x.Body == "gate_shown|panel|dtrh|basic");
            Assert.Equal(6, System.Linq.Enumerable.Count(sent, x => x.Body == "cta_clicked|panel|dtrh|patreon"));
            Assert.Equal(2, opened.Count); // neither unknown plan nor page-provided URL opens
        }
        finally
        {
            CoreSettings.Current.OfflineMode = offline;
            FunnelPageMessage.ResetForTests();
        }
    }

    [Fact]
    public void GameHostForwardsClicksAndBlocksRunsExitAndClose() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        FunnelPageMessage.ResetForTests();
        bool offline = CoreSettings.Current.OfflineMode;
        var opened = new List<string>();
        int counts = 0;
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        GameWindow? running = null, exiting = null;
        try
        {
            CoreSettings.Current.OfflineMode = false;
            FunnelPageMessage.BaseUrlOverride = "http://127.0.0.1:9";
            FunnelPageMessage.SendOverride = (_, _) => { Interlocked.Increment(ref counts); return Task.CompletedTask; };
            FunnelPageMessage.OpenCheckout = url => { opened.Add(url); return true; };
            FunnelPageMessage.UtcNow = () => now;
            var cta = Message("cta_clicked", "basic").ToString();
            running = new GameWindow(GameWindow.Games["dtrh"]);
            running.Show();
            running.HandleMessage(cta);
            Assert.Equal(PlanCatalog.Current.Basic.PatreonUrl, Assert.Single(opened));
            now = now.AddSeconds(4);
            running.HandleMessage("{\"type\":\"run-started\"}");
            running.HandleMessage(cta);
            running.Close();
            running.HandleMessage(cta);
            exiting = new GameWindow(GameWindow.Games["dtrh"]);
            exiting.Show();
            exiting.HandleMessage("{\"type\":\"exit\"}");
            exiting.HandleMessage(cta);
            exiting.Close();
            exiting.HandleMessage(cta);
            Assert.Single(opened);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref counts) == 5, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            running?.Close();
            exiting?.Close();
            CoreSettings.Current.OfflineMode = offline;
            FunnelPageMessage.ResetForTests();
        }
    });

    [Theory]
    [InlineData("basic", "patreon")]
    [InlineData("prime", "other")]
    [InlineData("basic", "substar")]
    [InlineData("prime", "web")]
    public void NamedPlansAndRailsResolveFromTheCatalog(string key, string rail)
    {
        FunnelPageMessage.ResetForTests();
        string? opened = null;
        try
        {
            FunnelPageMessage.OpenCheckout = url => { opened = url; return true; };
            FunnelPageMessage.Handle(Message("cta_clicked", key, rail), true);
            var plan = PlanCatalog.Current.ForKey(key)!;
            Assert.Equal(rail == "web" ? PlanCatalog.WebUrlFor(plan, "panel") : rail == "substar" ? plan.SubstarUrl : plan.PatreonUrl, opened);
        }
        finally { FunnelPageMessage.ResetForTests(); }
    }

    [Fact]
    public async Task InvalidEventsOfflineModeAndSandboxNeverSendOrOpen()
    {
        FunnelPageMessage.ResetForTests();
        bool offline = CoreSettings.Current.OfflineMode;
        var shell = ExternalOpener.Shell;
        int sends = 0, opens = 0;
        try
        {
            FunnelPageMessage.BaseUrlOverride = "http://127.0.0.1:9";
            FunnelPageMessage.SendOverride = (_, _) => { Interlocked.Increment(ref sends); return Task.CompletedTask; };
            ExternalOpener.Shell = _ => { opens++; return true; };
            CoreSettings.Current.OfflineMode = true;
            FunnelPageMessage.Handle(Message("gate_shown"), true);
            CoreSettings.Current.OfflineMode = false;
            FunnelPageMessage.Handle(Message("purchase", "basic"), true);
            FunnelPageMessage.BaseUrlOverride = "https://untrusted.invalid";
            FunnelPageMessage.Handle(Message("cta_clicked", "basic"), true);
            await Task.Delay(150);
            Assert.Equal(0, Volatile.Read(ref sends));
            Assert.Equal(0, opens); // ExternalOpener blocks public URLs in the test sandbox
        }
        finally
        {
            CoreSettings.Current.OfflineMode = offline;
            ExternalOpener.Shell = shell;
            FunnelPageMessage.ResetForTests();
        }
    }
}
