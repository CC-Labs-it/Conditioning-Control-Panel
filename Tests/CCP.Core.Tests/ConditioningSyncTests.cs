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
    public void ReadBeforeWriteKeepsHigherMinutesAndPushCarriesThem(double local, double server, double expected)
    {
        var s = new AppSettings { TotalConditioningMinutes = local };
        var user = JsonConvert.DeserializeObject<V2AuthService.V2User>(
            "{\"total_conditioning_minutes\":" + server.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")!;
        ProfileAdopt.AdoptReadBeforeWrite(s, user);
        Assert.Equal(expected, s.TotalConditioningMinutes);
        s.TotalConditioningMinutes = local;
        ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = JObject.FromObject(user) }, System.DateTime.UtcNow);
        Assert.Equal(expected, s.TotalConditioningMinutes);
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null)));
        Assert.Equal(expected, body.Value<double>("total_conditioning_minutes"));
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
            ProfileAdopt.AdoptReadBeforeWrite(s, User(20));
            Assert.Equal(0, lifts);
            ProfileAdopt.AdoptReadBeforeWrite(s, User(30));
            Assert.Equal(1, lifts);
            ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = JObject.FromObject(User(40)) }, System.DateTime.UtcNow);
            Assert.Equal(2, lifts);
            ProfileAdopt.ApplySyncResponse(s, new JObject { ["user"] = JObject.FromObject(User(40)) }, System.DateTime.UtcNow);
            Assert.Equal(2, lifts);
        }
        finally { ProfileAdopt.ConditioningMinutesLifted -= OnLift; }
    }
}
