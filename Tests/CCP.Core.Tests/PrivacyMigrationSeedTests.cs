using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Decision 2026-10-22 (option C, oracle-deep): loading a WPF-written settings.json seeds privacy ownership
/// for its UnifiedId, only for the switches the file actually carried. Everything goes through SettingsService.Load.</summary>
[Collection(SessionStatics.Name)]
public sealed class PrivacyMigrationSeedTests
{
    private static readonly string[] SixKeys =
        { "allow_discord_dm", "show_online_status", "share_profile_picture", "public_share_avatar", "goon_share_avatar", "goon_share_dm" };

    /// <summary>A file as the WPF release wrote it: every AppSettings key except the consent-ownership pair.</summary>
    private static JObject WpfFile(string? unifiedId = "wpf-user")
    {
        var s = new AppSettings
        {
            UnifiedId = unifiedId, AllowDiscordDm = true, ShowOnlineStatus = false, ShareProfilePicture = true,
            PublicShareRealAvatar = true, GoonShareAvatar = true, GoonShareDiscordDm = true,
        };
        var o = JObject.Parse(JsonConvert.SerializeObject(s));
        o.Remove("consentOwnedAccount");
        o.Remove("consentOwnedFields");
        return o;
    }

    private static AppSettings Load(JObject? file)
    {
        Directory.CreateDirectory(CorePaths.UserData);
        // The profile is shared by the assembly: keep whatever settings files earlier tests left and put them back (P02).
        var kept = Directory.GetFiles(CorePaths.UserData, "settings*").ToDictionary(f => f, File.ReadAllBytes);
        foreach (var f in kept.Keys) File.Delete(f);
        if (file != null) File.WriteAllText(Path.Combine(CorePaths.UserData, "settings.json"), file.ToString());
        try { return new SettingsService().Current; }
        finally
        {
            foreach (var f in Directory.GetFiles(CorePaths.UserData, "settings*")) File.Delete(f);
            foreach (var (f, bytes) in kept) File.WriteAllBytes(f, bytes);
        }
    }

    private static JObject SyncBodyOf(AppSettings s) =>
        JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null, consent: SyncPush.ConsentFields(s))));

    [Fact]
    public void MigratedWpfFileWithAllSixSendsAllSix()
    {
        var s = Load(WpfFile());
        Assert.Equal("wpf-user", s.ConsentOwnedAccount);
        var body = SyncBodyOf(s);
        Assert.All(SixKeys, k => Assert.Equal(JTokenType.Boolean, body[k]?.Type));
        Assert.False(body.Value<bool>("show_online_status"));
        Assert.True(body.Value<bool>("goon_share_dm"));
    }

    [Fact]
    public void FreshInstallSendsNone()
    {
        var s = Load(null);
        s.UnifiedId = "signed-in-later";
        Assert.Null(s.ConsentOwnedAccount);
        Assert.All(SixKeys, k => Assert.Null(SyncBodyOf(s)[k]));
    }

    [Fact]
    public void AnotherAccountSendsNone()
    {
        var s = Load(WpfFile());
        s.UnifiedId = "someone-else";
        Assert.All(SixKeys, k => Assert.Null(SyncBodyOf(s)[k]));
    }

    [Fact]
    public void NoUnifiedIdOrAConsentAwareFileSeedsNothing()
    {
        Assert.Null(Load(WpfFile(unifiedId: null)).ConsentOwnedAccount);
        var saved = WpfFile();
        saved["consentOwnedFields"] = 0;   // a consent-aware build already saved this file: scoped, never re-seeded
        var s = Load(saved);
        Assert.Null(s.ConsentOwnedAccount);
        Assert.All(SixKeys, k => Assert.Null(SyncBodyOf(s)[k]));
    }

    [Theory]
    [InlineData("goonShareAvatar", null)]      // key missing
    [InlineData("goonShareDiscordDm", "yes")]  // not a bool: not read
    [InlineData("AllowDiscordDm", null)]
    public void PartialFileSeedsNothing(string key, string? value)
    {
        var file = WpfFile();
        if (value == null) file.Remove(key); else file[key] = value;
        var s = Load(file);
        Assert.Null(s.ConsentOwnedAccount);
        Assert.Equal(0, s.ConsentOwnedFields);
        Assert.All(SixKeys, k => Assert.Null(SyncBodyOf(s)[k]));
    }
}
