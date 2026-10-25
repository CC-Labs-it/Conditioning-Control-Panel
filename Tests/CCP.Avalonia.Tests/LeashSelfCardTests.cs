using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using Runner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace CCP.Avalonia.Tests;

public sealed class LeashSelfCardTests
{
    private sealed class Api : ILeashApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public bool Offline;
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, (JObject)body.DeepClone()));
            return Task.FromResult<JObject?>(Offline ? null : new JObject { ["ok"] = true });
        }
    }
    private sealed class WatchRunner : Runner
    {
        public Assignment? Watched;
        public bool IsRunning => Watched != null;
        public string? RunningPid => null;
        public string? RunningAid => Watched?.Aid;
        public bool Start(Punishment p) => false;
        public bool StartAssignmentWatch(Assignment a) { Watched = a; return true; }
        public void Cancel() => Watched = null;
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }

    [Fact]
    public void PageRebindsWhenServiceArrives_AndCutsWithoutConfirmation_StalePollCannotRestore() => LeashGateCardTests.Run(true, (shell, _, _) =>
    {
        LeashHead.Service = null;
        shell.ShowTab("leash");
        var page = shell.FindControl<LeashTabView>("LeashTab")!;
        Assert.True(page.ShowingEmpty);
        var svc = new LeashService(new Api { Offline = true }, () => "u_me");
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        LeashHead.Service = svc;                      // built while the page is already showing
        Assert.False(page.ShowingEmpty);
        var card = page.GetLogicalDescendants().OfType<LeashSelfCard>().Single();
        var cut = card.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "leash-cut");
        LeashGateCardTests.Click(cut);
        Assert.Null(svc.Snapshot.Me);
        Assert.True(page.ShowingEmpty);
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment()); // stale poll cannot restore a cut leash
        Assert.True(page.ShowingEmpty);
    });

    [Fact]
    public void WaitingAndShelfFollowPollData_AndEmptyStateReturns() => LeashGateCardTests.Run(true, (shell, _, svc) =>
    {
        shell.ShowTab("leash");
        var page = shell.FindControl<LeashTabView>("LeashTab")!;
        var card = page.GetLogicalDescendants().OfType<LeashSelfCard>().Single();
        var me = svc.Snapshot.Me!;
        var stickers = Enumerable.Range(0, 16).Select(i => new Sticker("sticker-" + i, me.Holder, DateTimeOffset.UtcNow)).ToArray();
        card.Update(me with { Stickers = stickers });
        var lines = card.GetLogicalDescendants().OfType<TextBlock>().ToArray();
        Assert.Contains(lines, t => (string?)t.Tag == "leash-self-pending");
        Assert.Contains(lines, t => (string?)t.Tag == "leash-self-pardons");
        var shelf = lines.Where(t => (string?)t.Tag == "leash-sticker").ToArray();
        Assert.Equal(14, shelf.Length);
        Assert.StartsWith("sticker-15", shelf[0].Text);
        Assert.StartsWith("sticker-2", shelf[13].Text);
        card.Update(me with { Pending = Array.Empty<Punishment>(), Pardons = 0 });
        Assert.Contains(card.GetLogicalDescendants().OfType<TextBlock>(), t => (string?)t.Tag == "leash-self-clear");
        svc.ApplyBlock(null);
        Assert.True(page.ShowingEmpty);
    });

    [Fact]
    public void PlayerControlsSendLimits_AndWatchButtonReachesRunner() => LeashGateCardTests.Run(true, (_, _, _) =>
    {
        var api = new Api();
        var svc = new LeashService(api, () => "u_me");
        var block = LeashHeadTests.BlockWithPunishment();
        block["me"]!["assignment"] = new JObject {
            ["aid"] = "a1", ["kind"] = "video", ["size"] = 1,
            ["watch"] = new JObject { ["kind"] = "ht", ["id"] = "42" },
            ["day"] = LeashAssignRule.DayKey(DateTime.Now), ["status"] = "open", ["at"] = DateTimeOffset.UtcNow.ToString("o") };
        svc.ApplyBlock(block);
        var card = new LeashSelfCard(svc.Snapshot.Me!, () => svc);
        LeashGateCardTests.Click(card.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "leash-level:Soft"));
        LeashGateCardTests.Click(card.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "leash-dnd:FourHours"));
        var cap = card.GetLogicalDescendants().OfType<Slider>().Single();
        cap.Value = 17;
        cap.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right });
        Assert.Contains(api.Calls, c => c.Op == "settings" && (string?)c.Body["intensity"] == "soft");
        Assert.Contains(api.Calls, c => c.Op == "settings" && (string?)c.Body["dnd"] == "4h");
        Assert.Contains(api.Calls, c => c.Op == "settings" && (int?)c.Body["video_max"] == 17);
        var old = LeashTaskHost.Runner;
        var runner = new WatchRunner();
        LeashTaskHost.Runner = runner;
        try
        {
            var watch = card.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "leash-self-watch");
            LeashGateCardTests.Click(watch);
            Assert.Equal("a1", runner.Watched?.Aid);
            Assert.False(card.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == "leash-self-watch").IsEnabled);
            card.CutAsync().GetAwaiter().GetResult();
            Assert.Null(runner.Watched);
        }
        finally { LeashTaskHost.Runner = old; }
    });
}
