using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using Step = ConditioningControlPanel.Services.ProfileEquipStep;

namespace ConditioningControlPanel.Tests;

/// <summary>Frame and avatar through /v2/equip family profile (shared shop contract C5).</summary>
public class ProfileEquipSyncTests
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

    [Fact]
    public void SanitizeOwnKeepsShopIds_OnTheTwoSlotProbe()
    {
        var clean = CosmeticsCatalog.SanitizeOwn(new ProfileCosmetics { FrameId = "prime.frame.room-host", AvatarId = "profile.avatar.emi-pixel" });
        Assert.Equal(Host, ProfileSlots.Of(clean));
        var lace = CosmeticsCatalog.SanitizeOwn(new ProfileCosmetics { FrameId = "profile.frame.lace" });
        Assert.Equal(Lace, ProfileSlots.Of(lace));
        Assert.Equal(None, ProfileSlots.Of(CosmeticsCatalog.SanitizeOwn(new ProfileCosmetics { FrameId = "not.a.frame", AvatarId = "not-an-avatar" })));
    }

    [Fact]
    public void TheEquipStateIsMachineLocal()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ProfileEquipAccount)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ProfileEquipUpdatedMs)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ProfileEquipPushOwed)));
        var current = new AppSettings { ProfileEquipAccount = A, ProfileEquipUpdatedMs = 7, ProfileEquipPushOwed = true };
        var restored = new AppSettings { ProfileEquipAccount = B, ProfileEquipUpdatedMs = 99 };
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.Equal(A, restored.ProfileEquipAccount);
        Assert.Equal(7, restored.ProfileEquipUpdatedMs);
        Assert.True(restored.ProfileEquipPushOwed);
    }

    [Fact]
    public void TheWiringStays()
    {
        var root = RepoRoot();
        var save = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.ProfileCosmetics.cs"));
        Assert.Contains("ProfileEquipSync.Saved(slotsBefore, chosen)", save);
        // A sync echo must not refill a loadout whose clear is still on its way up.
        var sync = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Settings", "ProfileSyncService.cs"));
        Assert.Contains("if (settings.ProfileEquipPushOwed) return false;", sync);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
