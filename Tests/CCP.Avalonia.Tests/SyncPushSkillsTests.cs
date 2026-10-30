using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// wire-shell-login-b: skills, skill points, the force_skills_reset ack and the cloud loadout ride the push only once a
/// /v2/user/sync response this session carried the server's skills and they were adopted (WPF SyncProfileAsync:1698-1954).
/// Same fake wire and statics as <see cref="AccountSeedTests"/>.
/// </summary>
public sealed partial class AccountSeedTests
{
    private static readonly string[] SkillKeys = { "skill_points", "unlocked_skills", "force_skills_reset", "cosmetics" };

    private static void FreshTree()
    {
        var s = CoreSettings.Current;
        (s.SkillPoints, s.UnlockedSkills, s.PendingSkillsResetAck, s.ProfileCosmetics) = (0, new List<string>(), false, new ProfileCosmetics());
    }

    [Fact]
    public void FreshInstall_SendsNoSkills_UntilAResponseWasAdopted_ThenTheMergedValues() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        FreshTree();
        wire.SyncReply = """{"success":true,"skill_points":7,"unlocked_skills":["server_a","server_b"]}""";
        Assert.True(await AccountSeed.LoadProfileAsync());

        var first = Assert.Single(wire.Syncs);                   // defaults never go up before an adopt
        foreach (var k in SkillKeys) Assert.Null(first[k]);
        var s = CoreSettings.Current;
        Assert.True(sync.Adopted);
        Assert.Equal(7, s.SkillPoints);                          // MergeMax(7, 0)
        Assert.Equal(new[] { "server_a", "server_b" }, s.UnlockedSkills);

        s.SkillPoints = 9;                                       // earned locally since
        s.UnlockedSkills.Add("local_c");
        wire.SyncReply = """{"success":true,"skill_points":8,"unlocked_skills":["server_a"]}""";
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        var next = wire.Syncs.Last();
        Assert.Equal(9, (int)next["skill_points"]!);
        Assert.Equal(new[] { "server_a", "server_b", "local_c" }, next["unlocked_skills"]!.Values<string>());
        Assert.Null(next["force_skills_reset"]);
        Assert.Equal(9, s.SkillPoints);                          // a lower server echo never lowers it
        Assert.Equal(3, s.UnlockedSkills.Count);                 // nor drops a skill

        await AccountSeed.Logout();                              // a logout disarms it until the next adopt
        Assert.False(sync.Adopted);
    });

    [Fact]
    public void AResponseWithoutTheServersSkills_NeverArmsTheSkillFields() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        FreshTree();
        wire.SyncReply = """{"success":true,"skill_points":7}""";   // no unlocked_skills: the tree is not known
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.False(sync.Adopted);
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        foreach (var k in SkillKeys) Assert.Null(wire.Syncs.Last()[k]);
    });

    [Fact]
    public void ForceSkillsReset_ClearsTheTree_TakesTheServerPoints_AndAcksUntilTheFlagClears() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        FreshTree();
        var s = CoreSettings.Current;
        (s.SkillPoints, s.UnlockedSkills) = (3, new List<string> { "old_a" });
        wire.SyncReply = """{"success":true,"force_skills_reset":true,"skill_points":40,"unlocked_skills":[]}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Empty(s.UnlockedSkills);
        Assert.Equal(40, s.SkillPoints);
        Assert.True(s.PendingSkillsResetAck);

        wire.SyncReply = """{"success":true,"skill_points":40,"unlocked_skills":[]}""";   // the ack cleared it server-side
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.False((bool)wire.Syncs.Last()["force_skills_reset"]!);
        Assert.False(s.PendingSkillsResetAck);

        sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Null(wire.Syncs.Last()["force_skills_reset"]);
    });

    [Fact]
    public void TheCloudLoadout_FillsAnEmptyOne_NeverReplacesALocalPick() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        FreshTree();
        var s = CoreSettings.Current;
        wire.SyncReply = """{"success":true,"skill_points":0,"unlocked_skills":[],"cosmetics":{"banner_id":"bambi_neon_den"}}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Equal("bambi_neon_den", s.ProfileCosmetics.BannerId);

        s.ProfileCosmetics = new ProfileCosmetics();             // unequipped since: an adopted session sends the empty loadout
        wire.SyncReply = """{"success":true,"skill_points":0,"unlocked_skills":[]}""";
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.NotNull(wire.Syncs.Last()["cosmetics"]);

        s.ProfileCosmetics = new ProfileCosmetics { Accent = ProfileCosmetics.AccentSwatches[0] };
        wire.SyncReply = """{"success":true,"skill_points":0,"unlocked_skills":[],"cosmetics":{"banner_id":"bambi_neon_den"}}""";
        sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Null(s.ProfileCosmetics.BannerId);                // a stale echo never undresses a local pick
    });

    [Fact]
    public void AWebXpClaim_IsPaidOnce_AckedOnEveryPush_AndSkippedOnALevelReset() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        wire.SyncReply = """{"success":true,"web_xp":{"pending":0,"total":100,"claim":{"id":"c1","amount":100}}}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Null(wire.Syncs.Last()["web_xp_claim_ack"]);   // nothing applied yet
        var xp = ProfileAdopt.TotalXp(s);
        Assert.Equal("c1", s.LastWebXpClaimId);

        sync.UtcNow = () => T0.AddSeconds(31);                   // the same claim re-offered: acked, never paid twice
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Equal("c1", (string?)wire.Syncs.Last()["web_xp_claim_ack"]);
        Assert.Equal(xp, ProfileAdopt.TotalXp(s));

        wire.SyncReply = """{"success":true,"level_reset":true,"web_xp":{"claim":{"id":"c2","amount":50}}}""";
        sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Equal("c1", s.LastWebXpClaimId);                 // re-offered after the reset instead
    });

    [Fact]
    public void AWebXpClaim_AddsItsAmountWithoutMultipliers() => WithFreshInstall(async () =>
    {
        var (wire, _, _) = SignIn("u1", L40Profile("u1"));
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        wire.SyncReply = """{"success":true}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        var before = ProfileAdopt.TotalXp(s);
        SyncPush.ClaimWebXp(s, JObject.Parse("""{"web_xp":{"claim":{"id":"c9","amount":100}}}"""));
        Assert.Equal(before + 100, ProfileAdopt.TotalXp(s));
    });

    [Fact]
    public void AnAcceptedLevelReset_UnionsTheServersSkillsWithTheLocalTree() => WithFreshInstall(async () =>
    {
        var (wire, _, _) = SignIn("u1", L40Profile("u1"));
        FreshTree();
        var s = CoreSettings.Current;
        s.UnlockedSkills = new List<string> { "kept_a" };
        wire.SyncReply = """{"success":true,"level_reset":true,"skill_points":0,"unlocked_skills":["server_b"],"user":{"level":1,"xp":0,"current_season":"2026-08","highest_level_ever":1}}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Equal(1, s.PlayerLevel);
        Assert.Equal(new[] { "kept_a", "server_b" }, s.UnlockedSkills.OrderBy(x => x));
    });
}
