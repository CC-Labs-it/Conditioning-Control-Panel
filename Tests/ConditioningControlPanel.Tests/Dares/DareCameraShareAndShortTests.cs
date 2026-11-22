using System;
using System.Linq;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Tests.Dares.DareUiTestKit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>7.3.5 Dares round: the camera bar at 60%, the camera share on a running card, and a
/// Sparkles stake the player cannot cover (<c>short</c>).</summary>
public class DareCameraShareAndShortTests
{
    // ---- the bar ----

    [Fact]
    public void The_camera_bar_is_sixty_percent_and_the_help_says_so()
    {
        Assert.Equal(0.6, DareGrammar.CameraPassFraction);
        Assert.Equal(60, DareGrammar.CameraPassPercent);
        Assert.Equal("CCP looks for your face a few times a second, so keep it lit and in view. Seen 60% of the counted time = pass. Runs on your PC. No picture leaves it. Only the seconds are sent.",
            En("dares_help_camera"));
        Assert.Equal("seen 60% of the time = pass", En("dares_cam_pass"));
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var s = Strings(lang);
            Assert.DoesNotContain("80", s["dares_help_camera"]);
            Assert.DoesNotContain("80", s["dares_cam_pass"]);
            Assert.Contains("60", s["dares_help_camera"]);
        }
    }

    // ---- camera_seen on the wire ----

    [Fact]
    public void Camera_seen_reads_per_side_and_null_or_missing_reads_null()
    {
        var o = DaresApiCoreTests.BetJson();
        o["camera"] = true;
        o["camera_seen"] = new JObject { ["from"] = null, ["to"] = 41 };
        var b = DaresParse.Bet(o)!;
        Assert.Null(b.FromCameraSeen);
        Assert.Equal(41, b.ToCameraSeen);
        Assert.Equal(41, b.MyCameraSeen);

        o["camera_seen"] = new JObject { ["from"] = 140, ["to"] = -3 };
        b = DaresParse.Bet(o)!;
        Assert.Equal(100, b.FromCameraSeen);
        Assert.Equal(0, b.ToCameraSeen);

        o.Remove("camera_seen");   // an old server
        b = DaresParse.Bet(o)!;
        Assert.Null(b.FromCameraSeen);
        Assert.Null(b.ToCameraSeen);

        o["camera_seen"] = null;
        Assert.Null(DaresParse.Bet(o)!.ToCameraSeen);
    }

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

    // ---- the preview's drifting share ----

    [Fact]
    public void The_preview_share_drifts_across_the_bar_for_counting_sides_only()
    {
        var start = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var b = Bet(me: DareSide.To, camera: true, at: start);
        b = b with { AcceptedAt = start };
        Assert.Null(PreviewDaresService.FakeSeen(b, DareSide.From, start.AddMinutes(1)));   // the sender of a "can't" never counts
        Assert.Null(PreviewDaresService.FakeSeen(b, DareSide.To, start));                   // nothing counted yet
        Assert.Null(PreviewDaresService.FakeSeen(b with { Camera = false }, DareSide.To, start.AddMinutes(1)));
        var seen = Enumerable.Range(2, 120).Select(s => PreviewDaresService.FakeSeen(b, DareSide.To, start.AddSeconds(s))!.Value).ToList();
        Assert.Contains(seen, v => v < 60);
        Assert.Contains(seen, v => v >= 60);
        Assert.All(seen, v => Assert.InRange(v, 0, 100));
        Assert.NotNull(PreviewDaresService.FakeSeen(b with { Type = DareType.Duel }, DareSide.From, start.AddMinutes(1)));
    }
}
