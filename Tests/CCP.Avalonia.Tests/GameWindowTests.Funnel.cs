using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Vault;
using ConditioningControlPanel.Services.Funnel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void DtrhFunnelOpensKnownCheckoutOnlyAfterTheRun() => RunDtrh(true, (_, _, _, _) =>
    {
        ConditioningControlPanel.CoreSettings.Current.OfflineMode = true;
        FunnelPageMessage.Reset();
        var previous = GameWindow.DtrhOpenCheckout;
        int opened = 0;
        GameWindow.DtrhOpenCheckout = url => { Assert.Contains("patreon.com/checkout/CodeBambi", url); opened++; };
        try
        {
            var window = GameWindow.Launch("dtrh")!;
            window.HandleMessage("{\"type\":\"run-started\"}");
            const string message = "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"dtrh\",\"b\":\"card\",\"plan\":\"prime\"}";
            window.HandleMessage(message);
            Assert.Equal(0, opened);
            window.HandleMessage("{\"type\":\"run-ended\"}");
            window.HandleMessage(message);
            window.HandleMessage(message);
            Assert.Equal(1, opened);
        }
        finally { GameWindow.DtrhOpenCheckout = previous; FunnelPageMessage.Reset(); }
    });

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void DtrhSummaryOffersPrimeOnlyForDailyGiftRuns(bool hasLab, bool freeToday, bool expectRow) => RunDtrh(true, (_, _, _, _) =>
    {
        CoreSettings.Current.OfflineMode = true;
        FunnelPageMessage.Reset();
        var previous = GameWindow.DtrhOpenCheckout;
        var opened = new List<string>();
        GameWindow.DtrhOpenCheckout = opened.Add;
        try
        {
            var window = GameWindow.Launch("dtrh")!;
            CoreEntitlement.HasLabProvider = () => hasLab;
            CoreEntitlement.IsFreeTodayProvider = key => key == "dtrh" && freeToday;
            var sent = new List<JObject>();
            window.Posted += json => sent.Add(JObject.Parse(json));
            window.HandleMessage("{\"type\":\"run-started\"}");
            Assert.Empty(sent);
            window.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            var payout = Assert.Single(sent);
            Assert.Equal(expectRow, (bool?)payout["daily_gift"]);
            Assert.Equal(expectRow ? "prime" : null, (string?)payout["plan"]);
            var init = JObject.FromObject(window.DtrhInitMessage());
            Assert.Equal(PlanCatalog.ToWire(PlanCatalog.Current).ToString(), init["plans"]!.ToString());
            // Feed the real wire into the shipped page, build its row, then click its link.
            var click = DtrhSummaryClick(payout, init, expectRow);
            Assert.Empty(opened);
            if (click != null)
            {
                window.HandleMessage(click);
                Assert.Equal(PlanCatalog.Current.Prime.PatreonUrl, Assert.Single(opened));
            }
        }
        finally { GameWindow.DtrhOpenCheckout = previous; FunnelPageMessage.Reset(); }
    });

    private static string? DtrhSummaryClick(JObject payout, JObject init, bool expectRow,
        [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "Assets", "dtrh-summary-click.mjs"));
        using var process = Process.Start(start)!;
        process.StandardInput.Write(new JObject { ["payout"] = payout, ["init"] = init, ["expectRow"] = expectRow }.ToString(Formatting.None));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Summary page test timed out");
        }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        var click = output.GetAwaiter().GetResult().Trim();
        return click.Length == 0 ? null : click;
    }
}
