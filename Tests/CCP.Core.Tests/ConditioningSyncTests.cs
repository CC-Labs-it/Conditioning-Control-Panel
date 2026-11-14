using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ConditioningSyncTests
{
    [Theory]
    [InlineData(12.5, 20, 20)]
    [InlineData(25, 20, 25)]
    [InlineData(25, -1, 25)]
    [InlineData(25, double.NaN, 25)]
    [InlineData(25, double.PositiveInfinity, 25)]
    [InlineData(25, double.NegativeInfinity, 25)]
    public void ReadBeforeWriteKeepsHigherMinutesAndPushCarriesThem(double local, double server, double expected)
    {
        var s = new AppSettings { TotalConditioningMinutes = local };
        var user = JsonConvert.DeserializeObject<V2AuthService.V2User>(
            "{\"total_conditioning_minutes\":" + server.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")!;
        ProfileAdopt.AdoptReadBeforeWrite(s, user, adoptConditioningMinutes: true);
        Assert.Equal(expected, s.TotalConditioningMinutes);
        s.TotalConditioningMinutes = local;
        ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = new JObject(), ["total_conditioning_minutes"] = server }, System.DateTime.UtcNow);
        Assert.Equal(expected, s.TotalConditioningMinutes);
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null)));
        Assert.Equal(expected, body.Value<double>("total_conditioning_minutes"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncReadsTopLevelMinutesIndependentlyOfUser(bool includeUser)
    {
        var s = new AppSettings { TotalConditioningMinutes = 25 };
        var reply = JObject.Parse("{\"success\":true,\"total_conditioning_minutes\":40}");
        if (includeUser) reply["user"] = new JObject { ["total_conditioning_minutes"] = 999 };
        ProfileAdopt.ApplySyncResponse(s, reply, System.DateTime.UtcNow);
        Assert.Equal(40, s.TotalConditioningMinutes);
        // A nested user value alone is not the sync response's conditioning ledger.
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"user\":{\"total_conditioning_minutes\":999}}"), System.DateTime.UtcNow);
        Assert.Equal(40, s.TotalConditioningMinutes);
    }

    [Fact]
    public void DefaultReadBeforeWriteLeavesMinutesForWpfsOwnMerge()
    {
        var s = new AppSettings { TotalConditioningMinutes = 25 };
        var lifts = 0;
        void OnLift(AppSettings lifted) { if (ReferenceEquals(lifted, s)) lifts++; }
        ProfileAdopt.ConditioningMinutesLifted += OnLift;
        try
        {
            ProfileAdopt.AdoptReadBeforeWrite(s, new V2AuthService.V2User { TotalConditioningMinutes = 40 });
            Assert.Equal(25, s.TotalConditioningMinutes);
            Assert.Equal(0, lifts);
            // WPF ProfileSyncService.cs:2172 still sees the increase and can rebaseline at :2187.
            Assert.True(40 > s.TotalConditioningMinutes);
        }
        finally { ProfileAdopt.ConditioningMinutesLifted -= OnLift; }
    }

    // WPF ProfileSyncService.cs:2187 re-baselines the day log only when the merge lifted the minutes.
    [Fact]
    public void OnlyALiftRaisesTheDayLogRebaselineSignal()
    {
        var s = new AppSettings { TotalConditioningMinutes = 25 };
        var lifts = 0;
        void OnLift(AppSettings lifted) { if (ReferenceEquals(lifted, s)) lifts++; }
        ProfileAdopt.ConditioningMinutesLifted += OnLift;
        try
        {
            V2AuthService.V2User User(double m) => JsonConvert.DeserializeObject<V2AuthService.V2User>(
                "{\"total_conditioning_minutes\":" + m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")!;
            ProfileAdopt.AdoptReadBeforeWrite(s, User(20), adoptConditioningMinutes: true);
            Assert.Equal(0, lifts);
            ProfileAdopt.AdoptReadBeforeWrite(s, User(30), adoptConditioningMinutes: true);
            Assert.Equal(1, lifts);
            ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = new JObject(), ["total_conditioning_minutes"] = 40 }, System.DateTime.UtcNow);
            Assert.Equal(2, lifts);
            ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = new JObject(), ["total_conditioning_minutes"] = 40 }, System.DateTime.UtcNow);
            Assert.Equal(2, lifts);
        }
        finally { ProfileAdopt.ConditioningMinutesLifted -= OnLift; }
    }
}
