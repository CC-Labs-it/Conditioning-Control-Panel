using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Tests.Dares.DareUiTestKit;

namespace ConditioningControlPanel.Tests.Dares;

public class DareCameraRulesCoreTests
{
    [Fact]
    public void Short_is_its_own_answer_on_create_and_accept()
    {
        Assert.Equal(DareCreateStatus.Short, DaresParse.Create(new JObject { ["ok"] = true, ["status"] = "short" }).Status);
        Assert.Equal(DareActResult.Short, DaresParse.Act(new JObject { ["ok"] = true, ["status"] = "short" }));
        Assert.Equal(DareActResult.Short, DaresParse.Act(new JObject { ["ok"] = false, ["reason"] = "short" }));
        Assert.Equal("dares_create_short", DareCardRules.CreateKey(DareCreateStatus.Short));
        Assert.Equal("dares_act_short", DareCardRules.ActKey(DareActResult.Short));
        Assert.Equal("Not enough Sparkles to cover that stake.", En("dares_create_short"));
        Assert.Equal("Not enough Sparkles to cover your stake.", En("dares_act_short"));
    }

    // ---- the card line ----

    [Fact]
    public void A_running_camera_bet_shows_my_share_against_the_bar()
    {
        var below = Bet(me: DareSide.To, camera: true) with { ToCameraSeen = 41, FromCameraSeen = 99 };
        var line = DareCardRules.CameraRunning(below)!.Value;
        Assert.Equal("dares_cam_seen", line.Key);
        Assert.Equal(new object[] { 41, 60 }, line.Args);
        Assert.True(line.Below);
        Assert.Equal("camera 41% (needs 60%)", string.Format(En(line.Key), line.Args));

        var at = below with { ToCameraSeen = 60 };
        Assert.False(DareCardRules.CameraRunning(at)!.Value.Below);

        // My side only: a sender reads FromCameraSeen, never the friend's number.
        var sender = Bet(me: DareSide.From, camera: true) with { ToCameraSeen = 10 };
        Assert.Null(DareCardRules.CameraRunning(sender));
        Assert.True(DareCardRules.CameraRunning(sender with { FromCameraSeen = 12 })!.Value.Below);
    }

    [Fact]
    public void No_camera_nothing_counted_or_not_running_shows_no_line()
    {
        Assert.Null(DareCardRules.CameraRunning(Bet(me: DareSide.To, camera: false) with { ToCameraSeen = 41 }));
        Assert.Null(DareCardRules.CameraRunning(Bet(me: DareSide.To, camera: true)));
        Assert.Null(DareCardRules.CameraRunning(Bet(me: DareSide.To, camera: true, state: DareState.Sent) with { ToCameraSeen = 41 }));

        var settled = Bet(me: DareSide.To, camera: true, state: DareState.Settled, winner: DareSide.From) with { ToCameraSeen = 41 };
        Assert.Null(DareCardRules.CameraRunning(settled));
        var final = DareCardRules.CameraSettled(settled)!.Value;
        Assert.Equal("camera 41%, needed 60%", string.Format(En(final.Key), final.Args));
        Assert.Null(DareCardRules.CameraSettled(settled with { ToCameraSeen = null }));
        Assert.Equal("The camera can't see you. Light your face and stay in view.", En(DareCardRules.CameraHintKey));
    }

    // ---- covering a Sparkles stake ----

    [Fact]
    public void My_own_Sparkles_amounts_above_my_balance_are_locked_and_cannot_be_picked()
    {
        Assert.False(DareSlipRules.AmountCovered(DareStakeKind.Sp, DareStep.Mine, 200, 150));
        Assert.True(DareSlipRules.AmountCovered(DareStakeKind.Sp, DareStep.Mine, 100, 150));
        Assert.True(DareSlipRules.AmountCovered(DareStakeKind.Sp, DareStep.Mine, 150, 150));
        Assert.True(DareSlipRules.AmountCovered(DareStakeKind.Sp, DareStep.Theirs, 500, 0));   // the server checks theirs at accept
        Assert.True(DareSlipRules.AmountCovered(DareStakeKind.Sp, DareStep.Mine, 500, null));  // unknown: the server decides
        Assert.True(DareSlipRules.AmountCovered(DareStakeKind.Lock, DareStep.Mine, 30, 0));    // minutes are not Sparkles

        var d = new DareDraft { Who = Moth };
        DareSlipRules.SetType(d, DareType.Duel);
        DareSlipRules.PickKind(d, DareKind.Pics);
        DareSlipRules.SetDeadline(d, DareDeadline.Today);
        DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
        DareSlipRules.PickAmount(d, 500, balance: 120);
        Assert.Null(d.Mine);
        DareSlipRules.PickAmount(d, 100, balance: 120);
        Assert.Equal(new DareStake(DareStakeKind.Sp, 100), d.Mine);

        DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
        DareSlipRules.PickAmount(d, 500, balance: 120);
        Assert.Equal(new DareStake(DareStakeKind.Sp, 500), d.Theirs);
        Assert.Equal("You have 120 Sparkles.", string.Format(En("dares_sp_have"), 120));
    }

    [Fact]
    public void Accept_asks_whether_my_stake_is_covered()
    {
        var offer = Bet(me: DareSide.To, state: DareState.Sent, to: new DareStake(DareStakeKind.Sp, 200));
        Assert.False(DareCardRules.CanCover(offer, 150));
        Assert.True(DareCardRules.CanCover(offer, 200));
        Assert.True(DareCardRules.CanCover(offer, null));
        Assert.True(DareCardRules.CanCover(offer with { ToStake = new DareStake(DareStakeKind.Lock, 30) }, 0));
    }
}
