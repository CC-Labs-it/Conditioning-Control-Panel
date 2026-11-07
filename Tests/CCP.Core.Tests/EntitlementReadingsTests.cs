using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The #4073 grace rules as a head without EntitlementTierSync runs them.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class EntitlementReadingsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();

    public EntitlementReadingsTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private static AppSettings Signed(int tier) => new()
    {
        UnifiedId = "u1",
        AuthToken = "t",
        PatreonTier = tier,
        PatreonPremiumValidUntil = tier >= 1 ? Now.AddDays(13) : null,
        PatreonLabValidUntil = tier >= 2 ? Now.AddDays(13) : null,
    };

    private static bool Free() => false;

    [Fact]
    public void A_reading_with_a_tier_renews_the_grace()
    {
        // What the launch reset used to do by accident: a PayPal payer's stamp stays full.
        var s = Signed(2);
        s.PatreonPremiumValidUntil = Now.AddDays(1);
        s.PatreonLabValidUntil = Now.AddDays(1);

        var r = new EntitlementReadings().Apply(s, 2, Free, Now);

        Assert.Equal(EntitlementVerdict.Same, r.Verdict);
        Assert.False(r.Closed);
        Assert.True(r.Dirty);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonLabValidUntil);
    }

    [Fact]
    public void A_rise_stores_the_tier_and_stamps_it()
    {
        var s = Signed(0);
        var r = new EntitlementReadings().Apply(s, 2, Free, Now);

        Assert.True(r.Rose);
        Assert.True(r.Dirty);
        Assert.Equal(2, s.PatreonTier);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonLabValidUntil);
    }

    [Fact]
    public void One_lower_reading_closes_nothing()
    {
        var s = Signed(2);
        var r = new EntitlementReadings().Apply(s, 0, Free, Now);

        Assert.Equal(EntitlementVerdict.Lower, r.Verdict);
        Assert.False(r.Closed);
        Assert.Equal(2, s.PatreonTier);
        Assert.Equal(0, s.ServerTierSeen);
        Assert.Equal(Now.AddDays(13), s.PatreonLabValidUntil);
    }

    [Fact]
    public void The_same_lower_reading_90_seconds_later_closes_the_grace()
    {
        var s = Signed(2);
        var readings = new EntitlementReadings();
        readings.Apply(s, 0, Free, Now);

        Assert.False(readings.Apply(s, 0, Free, Now.AddSeconds(89)).Closed);
        var r = readings.Apply(s, 0, Free, Now.AddSeconds(90));

        Assert.True(r.Closed);
        Assert.True(r.Dirty);
        Assert.Equal(0, s.PatreonTier);
        Assert.Equal(2, s.GraceClosedTier);
        Assert.Equal(Now.AddSeconds(90), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddSeconds(90), s.PatreonLabValidUntil);
        Assert.Null(s.ServerTierSeen);
    }

    [Fact]
    public void A_session_or_game_holds_the_close_until_it_ends()
    {
        var s = Signed(2);
        var readings = new EntitlementReadings();
        readings.Apply(s, 0, () => true, Now);

        Assert.False(readings.Apply(s, 0, () => true, Now.AddMinutes(10)).Closed);
        Assert.Equal(2, s.PatreonTier);
        Assert.True(readings.Apply(s, 0, Free, Now.AddMinutes(12)).Closed);
    }

    [Fact]
    public void A_different_lower_reading_starts_the_wait_again()
    {
        var s = Signed(2);
        var readings = new EntitlementReadings();
        readings.Apply(s, 1, Free, Now);

        Assert.False(readings.Apply(s, 0, Free, Now.AddSeconds(100)).Closed);
        Assert.True(readings.Apply(s, 0, Free, Now.AddSeconds(190)).Closed);
    }

    [Fact]
    public void Another_account_starts_its_own_confirmation()
    {
        var s = Signed(2);
        var readings = new EntitlementReadings();
        readings.Apply(s, 0, Free, Now);

        s.UnifiedId = "u2";
        Assert.False(readings.Apply(s, 0, Free, Now.AddSeconds(100)).Closed);
        Assert.Equal(2, s.PatreonTier);
    }

    [Theory]
    [InlineData(null, "t")]
    [InlineData("u1", null)]
    public void Without_a_v2_identity_a_reading_lands_nowhere(string? uid, string? token)
    {
        var s = Signed(0);
        s.UnifiedId = uid;
        s.AuthToken = token;

        var r = new EntitlementReadings().Apply(s, 2, Free, Now);

        Assert.Equal(EntitlementVerdict.NoSignal, r.Verdict);
        Assert.False(r.Dirty);
        Assert.Equal(0, s.PatreonTier);
    }

    [Fact]
    public async Task A_signed_in_launch_keeps_the_tier_a_tokenless_one_does_not()
    {
        // The Discord patron: no Patreon tokens by construction, and a reading can land.
        var signedIn = Signed(2);
        await new ProviderSubscription("patreon", () => signedIn).InitializeAsync();
        Assert.Equal(2, signedIn.PatreonTier);
        Assert.NotNull(signedIn.PatreonLabValidUntil);

        // An id with no token: no reading can land, so the stored tier cannot stand.
        var noToken = Signed(2);
        _secrets.Clear();
        await new ProviderSubscription("patreon", () => noToken).InitializeAsync();
        Assert.Equal(0, noToken.PatreonTier);
        Assert.NotNull(noToken.PatreonLabValidUntil);   // the grace stays: a unified session exists
    }

    [Fact]
    public void A_closed_plan_says_subscribe_rather_than_reconnect()
    {
        var s = CoreSettings.Current;
        var had = TierGate.ReconnectIsTheAnswerProvider;
        try
        {
            TierGate.ReconnectIsTheAnswerProvider = () => true;
            Assert.True(TierGate.ReconnectIsTheAnswer());
            s.GraceClosedTier = 2;
            s.GraceClosedAtUtc = DateTime.UtcNow;
            Assert.False(TierGate.ReconnectIsTheAnswer());
            s.GraceClosedAtUtc = DateTime.UtcNow - EntitlementTierRule.ClosedRowWindow - TimeSpan.FromDays(1);
            Assert.True(TierGate.ReconnectIsTheAnswer());   // a link that broke later needs Reconnect back
        }
        finally
        {
            TierGate.ReconnectIsTheAnswerProvider = had;
            s.GraceClosedTier = null;
            s.GraceClosedAtUtc = null;
        }
    }

    [Fact]
    public void A_head_without_a_wallpaper_door_hides_its_palette_row()
    {
        var row = SettingsPaletteIndex.All.Single(e => e.Id == "rack.wallpaper");
        try
        {
            SettingsPaletteIndex.WallpaperDoorAvailableProvider = null;
            Assert.True(row.Available);
            SettingsPaletteIndex.WallpaperDoorAvailableProvider = () => false;
            Assert.False(row.Available);
        }
        finally { SettingsPaletteIndex.WallpaperDoorAvailableProvider = null; }
    }

    [Fact]
    public void No_tier_in_the_reading_changes_nothing()
    {
        var s = Signed(2);
        var r = new EntitlementReadings().Apply(s, null, Free, Now);

        Assert.False(r.Dirty);
        Assert.Equal(2, s.PatreonTier);
    }

    [Fact]
    public void Paying_again_after_a_close_is_an_ordinary_rise_and_clears_the_closed_row()
    {
        var s = Signed(2);
        var readings = new EntitlementReadings();
        readings.Apply(s, 0, Free, Now);
        readings.Apply(s, 0, Free, Now.AddSeconds(90));

        var r = readings.Apply(s, 2, Free, Now.AddHours(1));

        Assert.True(r.Rose);
        Assert.Equal(2, s.PatreonTier);
        Assert.Null(s.GraceClosedTier);
        Assert.Null(s.GraceClosedAtUtc);
    }
}
