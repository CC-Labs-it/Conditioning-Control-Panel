using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using static ConditioningControlPanel.Tests.Dares.DareUiTestKit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The drawer's folds (owner 2026-11-22): dares fold to one row and open to every bet newest
/// first, a settled row dismisses, the leash header counts what moved since it was last open, and
/// every new string is in all ten languages. Shares the drawer suite's fake and helpers.
/// </summary>
public partial class FriendsDrawerTests
{
    private sealed class FoldDares : IDaresService
    {
        public event Action? Changed { add { } remove { } }
        public DaresSnapshot Snapshot { get; set; } = Snap();
        public bool IsPreview => false;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
            => Task.FromResult(new DareCreateOutcome(DareCreateStatus.Sent));
        public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default)
            => Task.FromResult(DareActResult.Done);
    }

    private static readonly DateTimeOffset FoldNow = DateTimeOffset.UtcNow;

    private static DareBet[] FoldBets() => new[]
    {
        Bet("off", me: DareSide.To, state: DareState.Sent, at: FoldNow.AddMinutes(-10)),
        Bet("run", at: FoldNow.AddHours(-2)),
        Bet("s0", state: DareState.Passed, at: FoldNow.AddHours(-5), settledAt: FoldNow.AddHours(-1)),
        Bet("s1", state: DareState.Passed, at: FoldNow.AddHours(-6), settledAt: FoldNow.AddHours(-3)),
    };

    [Fact]
    public void Dare_fold_rules_one_row_on_top_chrono_when_open_and_only_settled_dismiss()
    {
        var snap = Snap(FoldBets());
        var none = new HashSet<string>();
        Assert.Equal("off", FriendsFold.DareFolded(snap, none)!.Id);
        Assert.Equal(new[] { "off", "s0", "run", "s1" }, FriendsFold.DaresChrono(snap, none).Select(b => b.Id));
        Assert.Equal(new[] { "off", "run", "s1" }, FriendsFold.DaresChrono(snap, new HashSet<string> { "s0" }).Select(b => b.Id));

        var bets = FoldBets();
        Assert.False(FriendsFold.CanDismiss(bets[0]));
        Assert.False(FriendsFold.CanDismiss(bets[1]));
        Assert.True(FriendsFold.CanDismiss(bets[2]));
    }

    [Fact]
    public void Dares_fold_to_one_row_open_to_all_and_a_dismissed_row_is_gone()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var source = FriendsDrawer.DaresSource;
            try
            {
                var dares = new FoldDares { Snapshot = Snap(FoldBets()) };
                FriendsDrawer.DaresSource = () => dares;
                var d = NewDrawer(new FakeFriends(Sample()));
                Assert.Equal(new[] { "off" }, d.DareRowIds);
                Assert.NotNull(Find(d.DaresStrip, "dares-more"));

                d.ToggleDares();
                Assert.True(d.DaresListOpen);
                Assert.Equal(new[] { "off", "s0", "run", "s1" }, d.DareRowIds);
                // Only the settled rows wear the x.
                Assert.Equal(2, Tags(d.DaresStrip).Count(t => t == "dares-dismiss"));

                d.DismissDare("s0");
                Assert.Equal(new[] { "off", "run", "s1" }, d.DareRowIds);
                Assert.Contains("s0", FriendsFold.State.DaresDismissed);

                // Everything dismissed and nothing open: no strip at all.
                dares.Snapshot = Snap(FoldBets()[2], FoldBets()[3]);
                d.DismissDare("s1");
                Assert.Equal(Visibility.Collapsed, d.DaresStrip.Visibility);
                Layout(d);
            }
            finally { FriendsDrawer.DaresSource = source; }
        });
    }

    private static LeashPerson P(string id, string name) => new(id, name, null);

    private static LeashSnapshot LeashWith(params HeldLeash[] holding)
    {
        var me = new MyLeash(P("juno", "Juno"), LeashIntensity.Standard, FoldNow.AddDays(-3), 3, null, LeashRemoteMode.Ask,
            new[] { new Punishment("p1", PunishKind.Lines, 10, null, P("juno", "Juno"), FoldNow, FoldNow.AddDays(1)) },
            new Assignment("a1", AssignKind.Minutes, 20, null, "2026-11-22", AssignStatus.Open, FoldNow), 0, Array.Empty<Sticker>());
        return new LeashSnapshot(me, holding, new[] { new LeashOffer(P("kit", "Kit"), FoldNow, FoldNow.AddDays(1)) { Id = "o1" } });
    }

    private static HeldLeash Held(string id, string name, AssignStatus status = AssignStatus.Done)
        => new(P(id, name), true, LeashIntensity.Standard, FoldNow.AddDays(-1), 1, null, null, Array.Empty<WeekDay>(),
            Array.Empty<Punishment>(), new Assignment("a-" + id, AssignKind.Quests, 2, null, "2026-11-22", status, FoldNow), 0);

    [Fact]
    public void Leash_fold_rules_count_what_moved_and_say_who_is_on_the_leash()
    {
        var snap = LeashWith(Held("ada", "Ada"), Held("bo", "Bo", AssignStatus.Open), Held("cy", "Cy"));
        IReadOnlyList<LeashSentItem> Sent(string id) => id != "ada" ? Array.Empty<LeashSentItem>() : new[]
        {
            new LeashSentItem("k1", LeashItemKind.Punish, "ada", "p9", LeashStep.Seen, FoldNow, FoldNow),
            new LeashSentItem("k2", LeashItemKind.Reward, "ada", "r9", LeashStep.Sent, FoldNow, FoldNow),
        };
        var keys = FriendsFold.LeashKeys(snap, Sent);
        Assert.Contains("o:o1", keys);
        Assert.Contains("p:p1", keys);
        Assert.Contains("a:a1", keys);
        Assert.Contains("ha:a-ada:Done", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("ha:a-bo", StringComparison.Ordinal));   // still open: nothing moved
        Assert.Contains("r:k1:Seen", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("r:k2", StringComparison.Ordinal));      // only sent: not news

        Assert.Equal(0, FriendsFold.LeashUnseen(keys, null));
        Assert.Equal(keys.Count - 1, FriendsFold.LeashUnseen(keys, new List<string> { "o:o1" }));

        var summary = FriendsFold.LeashSummary(snap);
        Assert.Equal(("friends_leash_held_by", "Juno"), summary[0]);
        Assert.Equal(("friends_leash_holding", "Ada, Bo +1"), summary[1]);
    }

    [Fact]
    public void Leash_header_folds_by_default_and_wears_a_bubble_until_opened()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            try
            {
                var leash = new Controls.Leash.FakeLeashService(NoOffer(LeashWith(Held("ada", "Ada"))));
                Controls.Leash.LeashLocator.Service = () => leash;
                var d = NewDrawer(new FakeFriends(Sample()));
                d.IsOpen = true;
                d.Render();

                // Folded on a first look (no offer waiting): one header line, who is on the leash, no bubble for what was already there.
                Assert.Equal(Visibility.Visible, d.LeashFold.Visibility);
                Assert.Equal(0, d.LeashBadge);
                Assert.Null(Find(d.LeashFold, "friends-fold-badge"));
                var summary = (TextBlock)Find(d.LeashFold, "friends-fold-summary")!;
                Assert.Contains("Juno", summary.Text);
                Assert.Contains("Ada", summary.Text);
                Assert.NotNull(Find(d.LeashFold, "leash-section"));
                Assert.False(IsShown(d, "leash-section"));

                // Bo's task got done: the bubble says 1.
                leash.Snapshot = NoOffer(LeashWith(Held("ada", "Ada"), Held("bo", "Bo")));
                Assert.Equal(1, d.LeashBadge);
                Assert.NotNull(Find(d.LeashFold, "friends-fold-badge"));

                // Opening reads it; folding again shows no bubble until something else moves.
                d.ToggleLeash();
                Assert.True(FriendsFold.State.LeashOpen);
                Assert.Equal(0, d.LeashBadge);
                d.ToggleLeash();
                Assert.Equal(0, d.LeashBadge);
                Layout(d);
            }
            finally { Controls.Leash.LeashLocator.Service = () => null; }   // never the saved getter: it reads itself (stack overflow)
        });
    }

    private static LeashSnapshot NoOffer(LeashSnapshot s) => s with { Offers = Array.Empty<LeashOffer>() };

    [Fact]
    public void Live_offer_rule_opens_the_leash_without_the_stored_switch()
    {
        var snap = LeashWith(Held("ada", "Ada"));
        var live = FriendsFold.LiveOfferKeys(snap, FoldNow);
        Assert.Equal(new[] { "o:o1" }, live);
        Assert.Empty(FriendsFold.LiveOfferKeys(snap, FoldNow.AddDays(2)));   // past its life: no longer opens anything
        Assert.True(FriendsFold.LeashShownOpen(false, live, new HashSet<string>()));
        Assert.False(FriendsFold.LeashShownOpen(false, live, new HashSet<string> { "o:o1" }));
        Assert.False(FriendsFold.LeashShownOpen(false, Array.Empty<string>(), new HashSet<string>()));
        Assert.True(FriendsFold.LeashShownOpen(true, Array.Empty<string>(), new HashSet<string>()));
    }

    [Fact]
    public void An_incoming_offer_opens_the_folded_leash_by_itself()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            try
            {
                var leash = new Controls.Leash.FakeLeashService(NoOffer(LeashWith(Held("ada", "Ada"))));
                Controls.Leash.LeashLocator.Service = () => leash;
                var d = NewDrawer(new FakeFriends(Sample()));
                d.IsOpen = true;
                d.Render();
                Assert.False(IsShown(d, "leash-section"));

                // Kit's offer arrives: the fold opens, the stored switch is untouched.
                leash.Snapshot = LeashWith(Held("ada", "Ada"));
                Assert.True(IsShown(d, "leash-section"));
                Assert.Equal(0, d.LeashBadge);
                Assert.False(FriendsFold.State.LeashOpen);

                // The player folds it: it stays folded for that offer, the stored switch still off.
                d.ToggleLeash();
                Assert.False(IsShown(d, "leash-section"));
                Assert.False(FriendsFold.State.LeashOpen);
                leash.Snapshot = LeashWith(Held("ada", "Ada"));
                Assert.False(IsShown(d, "leash-section"));

                // A press opens it the player's way (stored), and folding it again turns that off.
                d.ToggleLeash();
                Assert.True(IsShown(d, "leash-section"));
                Assert.True(FriendsFold.State.LeashOpen);
                d.ToggleLeash();
                Assert.False(FriendsFold.State.LeashOpen);
                Assert.False(IsShown(d, "leash-section"));

                // The offer gone, nothing pending: the leash stays folded.
                leash.Snapshot = NoOffer(LeashWith(Held("ada", "Ada")));
                Assert.False(IsShown(d, "leash-section"));
                Layout(d);
            }
            finally { Controls.Leash.LeashLocator.Service = () => null; }   // never the saved getter: it reads itself (stack overflow)
        });
    }

    /// <summary>True when the element tagged <paramref name="tag"/> sits under a visible chain.</summary>
    private static bool IsShown(FriendsDrawer d, string tag)
    {
        if (Find(d.LeashFold, tag) is not FrameworkElement e) return false;
        for (DependencyObject? x = e; x != null && !ReferenceEquals(x, d.LeashFold); x = LogicalTreeHelper.GetParent(x))
            if (x is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }

    [Fact]
    public void Every_fold_string_is_in_all_ten_languages()
    {
        var dir = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in FriendsFold.AllKeys)
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
        }
    }
}
