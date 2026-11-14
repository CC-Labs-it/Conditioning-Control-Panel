using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;
using Step = ConditioningControlPanel.Services.ProfileEquip.ProfileEquipStep;
using ProfileEquipSync = ConditioningControlPanel.Services.ProfileEquip.ProfileEquipRules;
using ConditioningControlPanel.Services.ProfileEquip;

namespace CCP.Core.Tests;

/// <summary>Frame and avatar through /v2/equip family profile (shared shop contract C5): WPF ProfileEquipSyncTests
/// against the Core rules the Avalonia head uses.</summary>
public class ProfileEquipRulesTests
{
    private const string A = "acct-a";
    private const string B = "acct-b";
    private static readonly ProfileSlots None = default;
    private static readonly ProfileSlots Lace = new("profile.frame.lace", null);
    private static readonly ProfileSlots Host = new("prime.frame.room-host", "profile.avatar.emi-pixel");

    private static Step Decide(string? stored, bool owed, ProfileSlots local, ProfileSlots? server, long serverMs, long localMs = 100)
        => ProfileEquipSync.Decide(stored, A, owed, local, server, serverMs, localMs);

    [Fact]
    public void ThePutCarriesBothSlots_ANullClears()
    {
        var body = ProfileEquipSync.BuildPut("u1", Lace);
        Assert.Equal("u1", (string?)body["unified_id"]);
        Assert.Equal("profile", (string?)body["family"]);
        Assert.Equal("profile.frame.lace", (string?)body["value"]!["frame_id"]);
        Assert.Equal(JTokenType.Null, body["value"]!["avatar_id"]!.Type);
    }

    [Fact]
    public void TheFirstRunPutLeavesEmptySlotsOut_SoItCannotClearAPickMadeElsewhere()
    {
        var value = ProfileEquipSync.BuildPut("u1", Lace, keepEmpty: true)["value"]!;
        Assert.Equal("profile.frame.lace", (string?)value["frame_id"]);
        Assert.Null(value["avatar_id"]);
    }

    [Fact]
    public void TheReplyReads_OrCountsAsNoCopy()
    {
        var iso = ProfileEquipSync.ReadProfile(JObject.Parse(
            "{\"ok\":true,\"equip\":{\"profile\":{\"frame_id\":\"prime.frame.room-host\",\"avatar_id\":\"profile.avatar.emi-pixel\"},\"updatedAt\":\"2026-10-31T12:00:00.000Z\"}}"));
        Assert.Equal(Host, iso!.Value.Slots);
        Assert.Equal(new System.DateTimeOffset(2026, 10, 31, 12, 0, 0, System.TimeSpan.Zero).ToUnixTimeMilliseconds(), iso.Value.UpdatedMs);

        var blank = ProfileEquipSync.ReadProfile(JObject.Parse(
            "{\"ok\":true,\"equip\":{\"profile\":{\"frame_id\":null,\"avatar_id\":\"  \"},\"updatedAt\":null}}"));
        Assert.Equal(None, blank!.Value.Slots);
        Assert.Equal(0, blank.Value.UpdatedMs);

        Assert.Null(ProfileEquipSync.ReadProfile(JObject.Parse("{\"ok\":false,\"reason\":\"bad_input\"}")));
        Assert.Null(ProfileEquipSync.ReadProfile(JObject.Parse("{\"ok\":true,\"equip\":{\"desk\":{}}}")));
        Assert.Null(ProfileEquipSync.ReadProfile(null));
    }

    [Fact]
    public void AnOlderServersEquipReplyIsStillAnAnswer_ARefusalIsNot()
    {
        Assert.True(ProfileEquipSync.IsEquipReply(JObject.Parse("{\"ok\":true,\"equip\":{\"desk\":{}}}")));
        Assert.False(ProfileEquipSync.IsEquipReply(JObject.Parse("{\"ok\":false}")));
        Assert.False(ProfileEquipSync.IsEquipReply(JObject.Parse("{\"ok\":true}")));
        Assert.False(ProfileEquipSync.IsEquipReply(null));
    }

    [Fact]
    public void APendingPickWinsOverANewerServerCopy()
    {
        // The chess gap (72-PR-RESULTS): another family's PUT moves the shared updatedAt.
        Assert.Equal(Step.Push, Decide(A, owed: true, Lace, Host, serverMs: 999));
        Assert.Equal(Step.Push, Decide(null, owed: true, Lace, Host, serverMs: 999));
        // A local pick stays ahead even if the saved clock is ahead of this PC.
        var settings = new AppSettings { ProfileEquipUpdatedMs = long.MaxValue - 2 };
        Assert.Equal(long.MaxValue - 1, ProfileEquipSync.MarkOwed(settings));
        Assert.True(settings.ProfileEquipPushOwed);
    }

    [Fact]
    public void ANewerServerCopyIsAdopted_NothingElseIs()
    {
        Assert.Equal(Step.Adopt, Decide(A, false, Lace, Host, serverMs: 200));
        Assert.Equal(Step.None, Decide(A, false, Lace, Host, serverMs: 100));
        Assert.Equal(Step.None, Decide(A, false, Lace, Host, serverMs: 50));
        Assert.Equal(Step.None, Decide(A, false, Lace, Host, serverMs: 0, localMs: 0));
        Assert.Equal(Step.None, Decide(A, false, Lace, null, serverMs: 0));
    }

    [Fact]
    public void ANewerStampOnEqualSlotsIsAnotherFamily_NotAPick()
    {
        Assert.Equal(Step.None, Decide(A, false, Lace, Lace, serverMs: 999));
        Assert.Equal(Step.None, Decide(A, false, None, None, serverMs: 999));
    }

    [Fact]
    public void ANewerEmptyServerCopyIsAClear()
    {
        Assert.Equal(Step.Adopt, Decide(A, false, Lace, None, serverMs: 200));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void FirstRun_AnExistingPickIsPushedBeforeAnyAdoption(long stamp)
    {
        // CCP-Server #391: the copy mirrors user.cosmetics before any PUT and the stamp is shared, so
        // neither one proves a pick was made elsewhere.
        Assert.Equal(Step.Push, Decide(null, false, Lace, null, stamp, localMs: 0));   // an older server
        Assert.Equal(Step.Push, Decide(null, false, Lace, None, stamp, localMs: 0));
        Assert.Equal(Step.Push, Decide(null, false, Lace, Lace, stamp, localMs: 0));
        Assert.Equal(Step.Push, Decide(null, false, Lace, Host, stamp, localMs: 0));
    }

    [Fact]
    public void FirstRun_NothingHere_TakesTheServersPick()
    {
        Assert.Equal(Step.Adopt, Decide(null, false, None, Host, serverMs: 0, localMs: 0));
        Assert.Equal(Step.Adopt, Decide(null, false, None, Host, serverMs: 500, localMs: 0));
    }

    [Fact]
    public void FirstRun_NothingEitherSide_DoesNothing()
    {
        Assert.Equal(Step.None, Decide(null, false, None, None, 0, 0));
        Assert.Equal(Step.None, Decide(null, false, None, null, 0, 0));
    }

    [Fact]
    public void AnotherAccountsStampAndPendingPick_MeanNothingHere()
    {
        // B's leftovers: its pending pick is not this account's, and is never pushed to it.
        Assert.Equal(Step.None, Decide(B, owed: true, Lace, null, 0));
        Assert.Equal(Step.None, Decide(B, owed: false, Lace, None, 999));
        Assert.Equal(Step.None, Decide(B, owed: true, Host, Host, 999));
        // This account's own pick lands, even against B's far newer stamp.
        Assert.Equal(Step.Adopt, Decide(B, owed: true, Lace, Host, serverMs: 1, localMs: 999_999));
    }

    [Fact]
    public void AdoptionChangesTheTwoSlotsOnly()
    {
        var current = new ProfileCosmetics
        {
            BannerId = "b1", Accent = "#FF69B4", TitleId = "t1", AvatarDeco = "d1", FrameId = "profile.frame.gilt",
            AvatarId = "old", PinnedAchievements = new List<string> { "p1" }, Charms = new List<string> { "c1" },
        };
        var next = ProfileEquipSync.WithSlots(current, new ProfileSlots("prime.frame.room-host", null));
        Assert.Equal("prime.frame.room-host", next.FrameId);
        Assert.Null(next.AvatarId);
        Assert.Equal("b1", next.BannerId);
        Assert.Equal("#FF69B4", next.Accent);
        Assert.Equal("t1", next.TitleId);
        Assert.Equal("d1", next.AvatarDeco);
        Assert.Equal(new[] { "p1" }, next.PinnedAchievements);
        Assert.Equal(new[] { "c1" }, next.Charms);
        Assert.Equal("profile.frame.gilt", current.FrameId);   // a copy, never the live object
    }

}
