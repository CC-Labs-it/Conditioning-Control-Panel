using System.Linq;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using Xunit;
using static ConditioningControlPanel.Tests.Dares.DareUiTestKit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The slip composer's rules: one blank at a time in the owner's order, the options each
/// blank offers, the numbering of sealed dares, the slip on the wire, and the sentence it reads as.</summary>
public class DareSlipUiTests
{
    private static DareDraft WithMoth() { var d = new DareDraft(); DareSlipRules.SetWho(d, Moth, 1); return d; }

    [Fact]
    public void Blanks_open_one_at_a_time_in_the_owners_order()
    {
        var d = new DareDraft();
        Assert.Equal(DareStep.Who, DareSlipRules.Current(d));
        DareSlipRules.SetWho(d, Moth);
        Assert.Equal(DareStep.How, DareSlipRules.Current(d));
        DareSlipRules.SetType(d, DareType.Cant);
        Assert.Equal(DareStep.What, DareSlipRules.Current(d));

        // A kind with amounts waits on its amount before the blank fills.
        DareSlipRules.PickKind(d, DareKind.Pics);
        Assert.Equal(DareKind.Pics, d.Pending);
        Assert.Equal(DareStep.What, DareSlipRules.Current(d));
        DareSlipRules.PickTarget(d, "500");
        Assert.Equal(DareStep.When, DareSlipRules.Current(d));

        DareSlipRules.SetDeadline(d, DareDeadline.Today);
        Assert.Equal(DareStep.Mine, DareSlipRules.Current(d));
        DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
        DareSlipRules.PickAmount(d, 100);
        Assert.Equal(DareStep.Theirs, DareSlipRules.Current(d));
        DareSlipRules.PickStakeKind(d, DareStakeKind.Dare);
        Assert.Equal(DareStep.Camera, DareSlipRules.Current(d));
        DareSlipRules.SetCamera(d, true);
        Assert.Null(DareSlipRules.Current(d));
        Assert.True(DareSlipRules.IsComplete(d));
    }

    [Theory]
    [InlineData(DareKind.Bubbles, DareType.Duel)]
    [InlineData(DareKind.Dare, DareType.Cant)]
    public void Kinds_without_presence_never_ask_for_the_camera(DareKind kind, DareType type)
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, type);
        DareSlipRules.PickKind(d, kind);
        Assert.DoesNotContain(DareStep.Camera, DareSlipRules.Steps(d));
        DareSlipRules.SetDeadline(d, DareDeadline.Week);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Dare);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Dare);
        Assert.True(DareSlipRules.IsComplete(d));
        Assert.False(DareSlipRules.ToSlip(d)!.Camera);
    }

    [Fact]
    public void A_duel_kind_fills_at_once_and_offers_no_targets()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Duel);
        Assert.Equal(new[] { DareKind.Pics, DareKind.Minutes, DareKind.Video, DareKind.Bubbles }, DareSlipRules.Kinds(d.Type));
        Assert.Empty(DareSlipRules.TargetsFor(DareType.Duel, DareKind.Pics));
        DareSlipRules.PickKind(d, DareKind.Video);
        Assert.Equal(new DareWhat(DareKind.Video, null), d.What);
        Assert.Null(d.Pending);
    }

    [Fact]
    public void Changing_the_bet_type_clears_what_it_was_about()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Cant);
        DareSlipRules.PickKind(d, DareKind.Session);
        DareSlipRules.PickTarget(d, "hard");
        DareSlipRules.SetType(d, DareType.Duel);
        Assert.Null(d.What);
        Assert.Equal(DareStep.What, DareSlipRules.Current(d));
    }

    [Fact]
    public void A_target_outside_the_table_is_ignored()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Cant);
        DareSlipRules.PickKind(d, DareKind.Pics);
        DareSlipRules.PickTarget(d, "999");
        Assert.Null(d.What);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
        Assert.Null(d.Mine);
    }

    [Fact]
    public void Tapping_a_filled_chip_reopens_it_with_its_amount_row()
    {
        var d = Whole();
        DareSlipRules.Edit(d, DareStep.Mine);
        Assert.Equal(DareStep.Mine, DareSlipRules.Current(d));
        Assert.Equal(DareStakeKind.Sp, d.Pending);
        DareSlipRules.PickAmount(d, 500);
        Assert.Equal(new DareStake(DareStakeKind.Sp, 500), d.Mine);
        Assert.Null(DareSlipRules.Current(d));
    }

    [Fact]
    public void Sealed_dares_are_numbered_in_order_of_appearance()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Cant);
        DareSlipRules.PickKind(d, DareKind.Dare);
        DareSlipRules.SetDeadline(d, DareDeadline.ThreeHours);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Lock);
        DareSlipRules.PickAmount(d, 20);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Dare);
        Assert.Equal((1, 0, 2), DareSlipRules.Numbers(d));
        var slip = DareSlipRules.ToSlip(d)!;
        Assert.Equal(new DareWhat(DareKind.Dare, "1"), slip.What);
        Assert.Equal(2, slip.ToStake.DareNumber);
        Assert.Equal(new DareStake(DareStakeKind.Lock, 20), slip.FromStake);
        Assert.True(DareSlipRules.HasDare(slip));
    }

    [Fact]
    public void No_slip_leaves_while_a_blank_is_open()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Cant);
        Assert.Null(DareSlipRules.ToSlip(d));
    }

    [Fact]
    public void The_sentence_reads_whole_once_every_blank_is_filled()
    {
        var lines = DareSlipRules.Sentence(Whole(), En);
        Assert.Equal("I bet Moth can't see 500 pictures today", lines[0].Plain);
        Assert.Equal("They make it: I owe 100 Sparkles", lines[1].Plain);
        Assert.Equal(DareMark.Yes, lines[1].Mark);
        Assert.Equal("They miss: they owe dare (1)", lines[2].Plain);
        Assert.Equal(DareMark.No, lines[2].Mark);
        Assert.Equal("camera check on", lines[3].Plain);
        Assert.All(lines.SelectMany(l => l.Segs).Where(s => s.Kind == DareSegKind.Chip), s => Assert.NotNull(s.TipKey));
    }

    [Fact]
    public void The_sentence_shows_only_up_to_the_blank_in_hand()
    {
        var d = new DareDraft();
        DareSlipRules.SetWho(d, Moth);
        var lines = DareSlipRules.Sentence(d, En);
        Assert.Single(lines);
        var segs = lines[0].Segs;
        Assert.Equal(DareSegKind.Chip, segs.Single(s => s.Step == DareStep.Who).Kind);
        Assert.Equal(DareSegKind.Blank, segs.Single(s => s.Step == DareStep.How).Kind);
        Assert.DoesNotContain(segs, s => s.Step is DareStep.What or DareStep.When);
    }

    [Fact]
    public void A_duel_reads_as_a_race_with_I_lose_they_lose()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Duel);
        DareSlipRules.PickKind(d, DareKind.Video);
        DareSlipRules.SetDeadline(d, DareDeadline.Weekend);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Lockdown);
        DareSlipRules.PickAmount(d, 20);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Lockdown);
        DareSlipRules.PickAmount(d, 20);
        var lines = DareSlipRules.Sentence(d, En);
        Assert.Equal("Me vs Moth: who watches more video this weekend", lines[0].Plain);
        Assert.Equal("I lose: I owe 20 min Lockdown", lines[1].Plain);
        Assert.Equal("They lose: they owe 20 min Lockdown", lines[2].Plain);
        Assert.Equal(DareStep.Camera, DareSlipRules.Current(d));
    }

    [Fact]
    public void A_receiver_reads_the_offer_from_their_own_side()
    {
        var bet = Bet(me: DareSide.To, state: DareState.Sent,
            from: new DareStake(DareStakeKind.Sp, 200), to: new DareStake(DareStakeKind.Lock, 10));
        var lines = DareSlipRules.Sentence(bet, En);
        Assert.Equal("Moth bets I can't see 500 pictures today", lines[0].Plain);
        Assert.Equal("I make it: they owe 200 Sparkles", lines[1].Plain);
        Assert.Equal("I miss: I owe 10 lock minutes", lines[2].Plain);
    }

    [Fact]
    public void Lockdown_is_an_ad_below_Basic_and_shut_on_a_free_friend()
    {
        var me = new DaresMe(null, false, true, 0);
        Assert.Equal(DareTileLock.Upsell, DareSlipRules.StakeLock(DareStakeKind.Lockdown, DareStep.Mine, me, iAmSub: false, friendTier: 2).Lock);
        Assert.Equal(DareTileLock.Open, DareSlipRules.StakeLock(DareStakeKind.Lockdown, DareStep.Mine, me, iAmSub: true, friendTier: 0).Lock);
        Assert.Equal((DareTileLock.Shut, "dares_shut_needs_sub"), DareSlipRules.StakeLock(DareStakeKind.Lockdown, DareStep.Theirs, me, true, 0));
        Assert.Equal(DareTileLock.Open, DareSlipRules.StakeLock(DareStakeKind.Lockdown, DareStep.Theirs, me, true, null).Lock);
        Assert.Equal(DareTileLock.Open, DareSlipRules.StakeLock(DareStakeKind.Sp, DareStep.Mine, me, false, 0).Lock);
    }

    [Fact]
    public void A_dare_needs_Discord_linked()
    {
        var me = new DaresMe(null, false, Discord: false, 0);
        Assert.Equal((DareTileLock.Shut, "dares_shut_discord"), DareSlipRules.StakeLock(DareStakeKind.Dare, DareStep.Mine, me, true, 1));
        Assert.Equal("dares_shut_discord", DareSlipRules.KindShut(DareKind.Dare, me));
        Assert.Null(DareSlipRules.KindShut(DareKind.Pics, me));
    }

    [Fact]
    public void Switching_to_a_free_friend_drops_a_Lockdown_on_their_side()
    {
        var d = WithMoth();
        d.Theirs = new DareStake(DareStakeKind.Lockdown, 10);
        DareSlipRules.SetWho(d, new DareFriend("u-juno", "Juno", null), 0);
        Assert.Null(d.Theirs);
    }

    [Fact]
    public void Helps_sit_on_the_blanks_that_hide_a_rule()
    {
        var d = WithMoth();
        Assert.Equal("dares_help_camera", DareSlipRules.HelpFor(DareStep.Camera, d));
        d.Pending = DareKind.Video;
        Assert.Equal("dares_help_video", DareSlipRules.HelpFor(DareStep.What, d));
        d.Pending = DareStakeKind.Lockdown;
        Assert.Equal("dares_help_lockdown", DareSlipRules.HelpFor(DareStep.Mine, d));
        d.Pending = DareStakeKind.Dare;
        Assert.Equal("dares_help_numbers", DareSlipRules.HelpFor(DareStep.Theirs, d));
    }

    private static DareDraft Whole()
    {
        var d = WithMoth();
        DareSlipRules.SetType(d, DareType.Cant);
        DareSlipRules.PickKind(d, DareKind.Pics);
        DareSlipRules.PickTarget(d, "500");
        DareSlipRules.SetDeadline(d, DareDeadline.Today);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
        DareSlipRules.PickAmount(d, 100);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Dare);
        DareSlipRules.SetCamera(d, true);
        return d;
    }
}
