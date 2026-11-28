using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Dialogs/Dares/DareOfferCard.cs and DareDebtCard.cs: consent before any Lockdown, and every
/// stop route closes both cards and cancels a start on the wire.</summary>
public sealed class DareCardTests
{
    private const string Bet = """
        {"id":"b1","role":"to","type":"cant","state":"settled","deadline":"3h","friend":{"id":"f","name":"Fen"},
        "what":{"kind":"pics","target":100},"ends_at":"2026-11-18T13:00:00Z","at":"2026-11-18T09:00:00Z",
        "accepted_at":"2026-11-18T10:00:00Z","camera":false,"stakes":{"from":{"kind":"sp","amount":50},
        "to":{"kind":"lockdown","amount":10}},"dares":[],"winner":"from","counts":{"from":0,"to":0},"debt":{"state":"owed"}}
        """;
    private static readonly string Offer = Bet.Replace("\"settled\"", "\"sent\"").Replace("\"b1\"", "\"o1\"")
        .Replace("\"winner\":\"from\"", "\"winner\":null").Replace("\"debt\":{\"state\":\"owed\"}", "\"debt\":null");

    private sealed class Api : IDaresApi
    {
        internal readonly TaskCompletionSource<JObject?> Start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Ops = new();
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Ops.Add(op);
            return op == "start" ? Start.Task
                : Task.FromResult<JObject?>(JObject.Parse("{\"ok\":true,\"me\":{},\"bets\":[" + Bet + "," + Offer + "]}"));
        }
    }

    private static Task Run(Func<Api, DarePanicTests.Host, DaresService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        PanicSurfacesTests.EnsureApp();
        Assert.Null(DareHead.Service);
        var api = new Api();
        var host = new DarePanicTests.Host();
        using var service = new DaresService(api, host, new DareMeter(), new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore()));
        var settings = CoreSettings.Current;
        var saved = (DareDebtCard.RunsAtStartup, settings.LockdownDisablePanicKey);
        DareHead.Service = service;
        DareDebtCard.RunsAtStartup = () => false;
        try
        {
            await service.RefreshAsync();
            await body(api, host, service);
        }
        finally
        {
            DareDebtCard.CloseForPanic();
            DareOfferCard.CloseForPanic();
            api.Start.TrySetResult(null);
            DareHead.Service = null;
            DareHead.Resume();
            (DareDebtCard.RunsAtStartup, settings.LockdownDisablePanicKey) = saved;
        }
    });

    private static DareDebtCard OpenDebt(DaresService service)
    {
        _ = DareDebtCard.ShowModal(null, service.Snapshot.Bets.Single(b => b.Id == "b1"));
        return DareDebtCard.Open!;
    }

    private static T Tagged<T>(Window w, string tag) where T : Control =>
        w.GetLogicalDescendants().OfType<T>().First(c => c.Tag as string == tag);

    private static void Press(Window w, string tag) => Tagged<Button>(w, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task Lockdown_starts_only_after_the_consent_is_given() => Run(async (api, host, service) =>
    {
        var card = OpenDebt(service);
        Press(card, "dares-pay");
        Assert.DoesNotContain("start", api.Ops);
        Press(card, "dares-consent-no");   // Not now: back to the card, nothing sent
        Assert.DoesNotContain("start", api.Ops);
        Assert.Null(card.LastPay);
        Press(card, "dares-pay");
        Press(card, "dares-consent-yes");
        Assert.Contains("start", api.Ops);
        api.Start.SetResult(new JObject { ["ok"] = true, ["status"] = "serving" });
        Assert.Equal(DareActResult.Done, await card.LastPay!);
        Assert.True(host.Active);
        Assert.False(card.IsVisible);
    });

    [Theory]
    [InlineData(false, "dares_ld_length_panic", "dares_offer_consent_panic")]
    [InlineData(true, "dares_ld_length", "dares_offer_consent")]
    public Task Consent_names_what_the_players_own_panic_setting_will_do(bool panicOff, string debtKey, string offerKey) => Run((_, _, service) =>
    {
        CoreSettings.Current.LockdownDisablePanicKey = panicOff;
        var card = OpenDebt(service);
        Press(card, "dares-pay");
        Assert.Equal("• " + Loc.GetF(debtKey, 10), Tagged<TextBlock>(card, "dares-consent-lockdown").Text);
        DareOfferCard.Present(null, "o1");
        Assert.Equal(Loc.GetF(offerKey, 10), Tagged<TextBlock>(DareOfferCard.Open!, "dares-offer-consent").Text);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("panic key")]
    [InlineData("tray")]
    [InlineData("safe word")]
    [InlineData("6-blink")]
    public Task Every_stop_route_closes_the_cards_and_cancels_a_pending_lockdown(string route) => Run(async (api, host, service) =>
    {
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey, s.BlinkRecalibrateShortcutEnabled, s.StrictLockEnabled);
        (s.PanicKeyEnabled, s.PanicKey, s.BlinkRecalibrateShortcutEnabled, s.StrictLockEnabled) = (true, "F8", true, false);
        var prev = PanicSurfaces.All;
        // Only the two Dares rows run for real; the rest of the pass is a no-op here.
        PanicSurfaces.All = prev.Select(x => x.Id is "dares-cards" or "dares" ? x : new PanicSurfaces.Surface(x.Id, _ => { })).ToArray();
        var shell = new MainShellWindow { BlinkRecalOfferForTests = () => Task.CompletedTask };
        shell.Show();
        DaresLanding.ResetForTests();   // the shell started the landing on this service; the test opens the cards itself
        StartupLadder.ResetForTests();
        try
        {
            DareOfferCard.Present(null, "o1");
            var offer = DareOfferCard.Open!;
            var card = OpenDebt(service);
            Press(card, "dares-pay");
            Press(card, "dares-consent-yes");
            Assert.Contains("start", api.Ops);
            var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Action stop = route switch
            {
                "panic key" => () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1)),
                "tray" => MainShellWindow.StopEverything,
                "safe word" => shell.VoicePanic,
                _ => () => { for (var i = 0; i < MainShellWindow.RapidBlinkRecalCount; i++) shell.OnRapidBlink(t = t.AddMilliseconds(500)); },
            };
            stop();
            Dispatcher.UIThread.RunJobs();   // one tick
            Assert.False(card.IsVisible, $"{route} left the debt card up");
            Assert.False(offer.IsVisible, $"{route} left the offer card up");
            Assert.Null(DareDebtCard.Open);
            api.Start.SetResult(new JObject { ["ok"] = true, ["status"] = "serving" });
            Assert.Equal(DareActResult.Refused, await card.LastPay!);
            Assert.False(host.Active);
        }
        finally
        {
            PanicSurfaces.All = prev;
            shell.Close();
            StartupLadder.ResetForTests();
            (s.PanicKeyEnabled, s.PanicKey, s.BlinkRecalibrateShortcutEnabled, s.StrictLockEnabled) = old;
        }
    });
}
