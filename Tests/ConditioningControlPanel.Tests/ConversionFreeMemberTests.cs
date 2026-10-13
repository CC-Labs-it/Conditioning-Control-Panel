using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup.Conversion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Free Patreon members (proposal 6b, brief C2 / C6): the profile's patreon_member, paid_until and
/// post_grants are read additively, a post grant opens its key until it ends, and a member's
/// second Daily Gift is never today's.
/// </summary>
public class ConversionFreeMemberTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_ReadsTheFourFieldsItKnows()
    {
        var user = JObject.Parse("{\"patreon_member\":true,\"paid_until\":\"2026-11-01T00:00:00Z\","
            + "\"post_grants\":{\"takeover\":\"2026-10-15T12:00:00Z\",\"bogus\":\"2026-10-15T12:00:00Z\",\"fyp\":\"nope\"}}");
        var r = ProfileExtras.Parse(user)!;
        Assert.True(r.PatreonMember);
        Assert.True(r.PaidUntilSent);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), r.PaidUntilUtc);
        Assert.Single(r.PostGrants!);
        Assert.True(r.PostGrants!.ContainsKey("takeover"));
    }

    [Fact]
    public void Parse_OlderServer_TouchesNothing()
    {
        var s = new AppSettings { PatreonMember = true, ServerPaidUntil = Now };
        s.PostGrants["voice"] = Now.AddDays(1).ToString("o");
        Assert.False(ProfileExtras.Apply(s, ProfileExtras.Parse(JObject.Parse("{\"effective_tier\":0}"))));
        Assert.True(s.PatreonMember);
        Assert.Equal(Now, s.ServerPaidUntil);
        Assert.Single(s.PostGrants);
    }

    [Fact]
    public void Apply_MovesOnlyWhatTheServerSent()
    {
        var s = new AppSettings { ServerPaidUntil = Now };
        Assert.True(ProfileExtras.Apply(s, ProfileExtras.Parse(JObject.Parse("{\"patreon_member\":true,\"paid_until\":null}"))));
        Assert.True(s.PatreonMember);
        Assert.Null(s.ServerPaidUntil);
        Assert.False(ProfileExtras.Apply(s, ProfileExtras.Parse(JObject.Parse("{\"patreon_member\":true,\"paid_until\":null}"))));
        Assert.True(ProfileExtras.Apply(s, ProfileExtras.Parse(JObject.Parse("{\"patreon_member\":false}"))));
        Assert.False(s.PatreonMember);
    }

    [Fact]
    public void GrantOpen_UntilItEnds()
    {
        var grants = new Dictionary<string, string> { ["takeover"] = Now.AddHours(1).ToString("o"), ["voice"] = Now.AddHours(-1).ToString("o") };
        Assert.True(ProfileExtras.GrantOpen(grants, "takeover", Now));
        Assert.True(ProfileExtras.GrantOpen(grants, "TAKEOVER", Now));
        Assert.False(ProfileExtras.GrantOpen(grants, "voice", Now));
        Assert.False(ProfileExtras.GrantOpen(grants, "fyp", Now));
        Assert.False(ProfileExtras.GrantOpen(null, "takeover", Now));
    }

    [Theory]
    [InlineData("takeover", "remote", "fyp", "remote")]      // server names one
    [InlineData("takeover", null, "fyp", "fyp")]             // tomorrow's seeded pick
    [InlineData("takeover", "takeover", "fyp", "fyp")]       // server named today's: ignored
    [InlineData("takeover", null, "takeover", "awareness")]  // tomorrow equals today: first other pool key
    public void MemberSecondKey_IsNeverToday(string today, string? server, string tomorrow, string expected)
    {
        var pool = new[] { "takeover", "awareness", "fyp", "remote" };
        Assert.Equal(expected, ProfileExtras.MemberSecondKey(today, server, tomorrow, pool));
    }

    [Fact]
    public void NewMemberSettings_RoundTripThroughJson()
    {
        var s = new AppSettings { PatreonMember = true, ServerPaidUntil = Now };
        s.PostGrants["dtrh"] = "2026-10-15T12:00:00.0000000Z";
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.True(back.PatreonMember);
        Assert.Equal("2026-10-15T12:00:00.0000000Z", back.PostGrants["dtrh"]);
        Assert.NotNull(back.ServerPaidUntil);
    }

    [Fact]
    public void Member_fields_never_ride_the_cloud_backup()
    {
        // They mirror the server profile; the next profile read refills them on any device.
        Assert.True(ConditioningControlPanel.Services.ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.PatreonMember)));
        Assert.True(ConditioningControlPanel.Services.ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.PostGrants)));
        Assert.True(ConditioningControlPanel.Services.ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ServerPaidUntil)));
    }
}
