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
}
