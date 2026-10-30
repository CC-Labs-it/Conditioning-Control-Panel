using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DescentAutoRestoreTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
    private static JObject Offer() => JObject.Parse("""{"user":{"level":150,"xp":999999,"curve_epoch":0},"descent_migration":{"required":true,"total_xp_earned":16343,"restore_basis_xp":70343,"devotion_days":180}}""");
    private static AppSettings Fixture() => new()
    {
        UnifiedId = "fixture", PlayerLevel = 40, PlayerXP = 123, HighestLevelEver = 150,
        LastConfirmedServerXp = 999999, LastConfirmedServerXpAccount = "fixture",
    };
    private static AppSettings Restart(AppSettings s) => JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;

    [Fact]
    public void Offer_runs_after_response_and_persists_before_submit_then_replay_preserves_earned_xp()
    {
        var s = Fixture();
        var saves = new System.Collections.Generic.List<AppSettings>();
        Assert.True(SyncPush.ApplyReply(s, Offer(), new DescentStage { Thresholds = new[] { 30, 90, 365 } }, Now,
            () => saves.Add(Restart(s))));
        Assert.Equal(70343, ProfileAdopt.TotalXp(s), 5);
        Assert.Equal(150, s.DescentPreMigrationLevel); // ordinary response handled before restore
        Assert.Equal(150, s.HighestLevelEver);
        Assert.Equal(70343, s.DescentPreMigrationLifetimeXp);
        Assert.Equal(1.1, DescentCycleXp.XpBonusFor(s));
        Assert.Equal(0, s.DescentCycle);
        Assert.True(s.DescentVeteranArchive);
        Assert.Equal(Now, s.DescentAnchorUtc);
        Assert.Equal(new[] { 1, 2 }, s.DescentPendingStageCeremonies);
        Assert.Null(s.DescentLastStageDripDate);
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.False(s.DescentMigrationCompleted);
        Assert.Equal(2, saves.Count);
        Assert.Null(saves[0].PendingDescentMigrationChoice);
        Assert.Equal("restore", saves[1].PendingDescentMigrationChoice);
        Assert.Equal(70343, ProfileAdopt.TotalXp(saves[0]), 5);
        s = Restart(s);
        s.PlayerXP += 110; // earned after applying and before ack
        var snapshot = JsonConvert.SerializeObject(s);
        Assert.False(SyncPush.ApplyReply(s, Offer(), null, Now.AddDays(1), () => throw new Exception("replay saved")));
        Assert.Equal(snapshot, JsonConvert.SerializeObject(s));
        Assert.Equal(70453, ProfileAdopt.TotalXp(s), 5);
    }

    [Fact]
    public void Crash_before_arming_replays_the_same_basis_and_ack_is_the_only_completion()
    {
        var s = Fixture();
        AppSettings? disk = null;
        Assert.Throws<InvalidOperationException>(() => SyncPush.ApplyReply(s, Offer(), null, Now, () =>
        {
            disk = Restart(s);
            throw new InvalidOperationException("power loss");
        }));
        Assert.Null(disk!.PendingDescentMigrationChoice);
        Assert.Equal(70343, ProfileAdopt.TotalXp(disk), 5);
        s = disk;
        Assert.True(SyncPush.ApplyReply(s, Offer(), null, Now, () => { }));
        Assert.Equal(70343, ProfileAdopt.TotalXp(s), 5);
        Assert.Empty(s.DescentPendingStageCeremonies);
        Assert.False(s.DescentMigrationCompleted);
        Assert.False(SyncPush.ApplyReply(s, JObject.Parse("""{"descent_migration":{"completed":true,"choice":"restore","required":true}}"""), null, Now, () => { }));
        Assert.True(s.DescentMigrationCompleted);
        Assert.Null(s.PendingDescentMigrationChoice);
        Assert.DoesNotContain("descent_migration", SyncPush.SerializeBody(s, null));
    }

    [Fact]
    public void Golden_body_advertises_auto_and_only_a_valid_pending_choice_rides_with_the_relevel()
    {
        var s = Fixture();
        SyncPush.ApplyReply(s, Offer(), null, Now, () => { });
        Assert.Equal("""{"unified_id":"fixture","xp":70343,"level":51,"descent_epoch":1,"descent_auto":true,"descent_migration":{"choice":"restore"}}""",
            SyncPush.SerializeBody(s, null));
        s.PendingDescentMigrationChoice = "RESTORE";
        Assert.DoesNotContain("descent_migration", SyncPush.SerializeBody(s, null));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"descent_migration\":null}")]
    [InlineData("{\"descent_migration\":{\"required\":false}}")]
    [InlineData("{\"descent_migration\":{\"required\":\"true\"}}")]
    public void Only_explicit_required_takes_an_offer(string reply)
    {
        var s = Fixture();
        var before = JsonConvert.SerializeObject(s);
        Assert.False(SyncPush.ApplyReply(s, JObject.Parse(reply), null, Now, () => throw new Exception("saved")));
        Assert.Equal(before, JsonConvert.SerializeObject(s));
    }

    [Fact]
    public void Full_choice_retains_cycle_semantics_and_rejects_invalid_or_already_completed()
    {
        var s = Fixture();
        var offer = new DescentMigrationOffer { TotalXpEarned = 16343, RestoreBasisXp = 70343 };
        Assert.False(DescentAutoRestore.ApplyChoice(s, "CYCLE", offer, null, Now, () => { }));
        Assert.True(DescentAutoRestore.ApplyChoice(s, "cycle", offer, null, Now, () => { }));
        Assert.Equal((1, 0d, 1, 150), (s.PlayerLevel, s.PlayerXP, s.DescentCycle, s.HighestLevelEver));
        Assert.Equal(16343, s.DescentPreMigrationLifetimeXp);
        Assert.Empty(s.DescentPendingStageCeremonies);
        s.PendingDescentMigrationChoice = null;
        s.DescentMigrationCompleted = true;
        Assert.False(DescentAutoRestore.ApplyChoice(s, "restore", offer, null, Now, () => { }));
    }
}
