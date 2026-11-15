using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class SyncReplyFlagsTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static void Apply(AppSettings s, string json) => ProfileAdopt.ApplySyncResponse(s, JObject.Parse(json), Now);

    [Theory]
    [InlineData("{\"oopsie_used_season\":\"2026-08\",\"oopsie_credits\":2,\"is_season0_og\":true,\"bonus_daily_rerolls\":3,\"level_reset\":true}", 3, 0)]
    [InlineData("{\"user\":null,\"oopsie_used_season\":\"2026-08\",\"oopsie_credits\":2,\"is_season0_og\":true,\"bonus_weekly_rerolls\":3,\"level_reset\":true}", 0, 3)]
    public void TopLevelFieldsApplyWithoutUserWithoutResettingProgress(string json, int daily, int weekly)
    {
        var s = new AppSettings { CurrentSeason = "2026-08", StreakFixCharges = 5,
            BonusDailyRerolls = 9, BonusWeeklyRerolls = 9, PlayerLevel = 42, PlayerXP = 10,
            HighestLevelEver = 50, UserDisplayName = "kept" };
        int notifications = 0;
        s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.StreakFixCharges)) notifications++; };
        ProfileAdopt.ApplySyncResponse(s, JObject.Parse(json), new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Multiple(
            () => Assert.True(s.SeasonalStreakRecoveryUsed),
            () => Assert.Equal(2, s.StreakFixCharges),
            () => Assert.Equal(1, notifications),
            () => Assert.True(s.IsSeason0Og),
            () => Assert.Equal(daily, s.BonusDailyRerolls),
            () => Assert.Equal(weekly, s.BonusWeeklyRerolls),
            () => Assert.Equal(42, s.PlayerLevel),
            () => Assert.Equal(10, s.PlayerXP),
            () => Assert.Equal(50, s.HighestLevelEver),
            () => Assert.Equal("kept", s.UserDisplayName));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    public void CreditsAreAuthoritativeAndNotify(int initial, int server)
    {
        var s = new AppSettings { StreakFixCharges = initial };
        int notifications = 0;
        s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.StreakFixCharges)) notifications++; };
        Apply(s, $"{{\"user\":{{}},\"oopsie_credits\":{server}}}");
        Assert.Equal(server, s.StreakFixCharges);
        Assert.Equal(1, notifications);
    }

    [Theory]
    [InlineData("2026-08", true)]
    [InlineData("2026-07", false)]
    public void OopsieUsesServerSeasonNotClock(string used, bool expected)
    {
        var s = new AppSettings { CurrentSeason = "2026-08", SeasonalStreakRecoveryUsed = !expected };
        Apply(s, $"{{\"user\":{{\"current_season\":\"2026-08\"}},\"oopsie_used_season\":\"{used}\"}}");
        Assert.Equal(expected, s.SeasonalStreakRecoveryUsed);
    }

    [Fact]
    public void NameIsAuthoritative()
    {
        var s = new AppSettings { UserDisplayName = "old" };
        Apply(s, "{\"user\":{\"display_name\":\"new\"}}");
        Assert.Equal("new", s.UserDisplayName);
    }

    [Fact]
    public void OgIsAuthoritative()
    {
        var s = new AppSettings { IsSeason0Og = true };
        Apply(s, "{\"user\":{},\"is_season0_og\":false}");
        Assert.False(s.IsSeason0Og);
    }

    [Fact]
    public void HighestIsAuthoritative()
    {
        var s = new AppSettings { HighestLevelEver = 90 };
        Apply(s, "{\"user\":{\"highest_level_ever\":12}}");
        Assert.Equal(12, s.HighestLevelEver);
    }

    [Theory]
    [InlineData("bonus_daily_rerolls", 3, 0)]
    [InlineData("bonus_weekly_rerolls", 0, 3)]
    public void MissingRerollDefaultsToZero(string field, int daily, int weekly)
    {
        var s = new AppSettings { BonusDailyRerolls = 9, BonusWeeklyRerolls = 9 };
        Apply(s, $"{{\"user\":{{}},\"{field}\":3}}");
        Assert.Equal(daily, s.BonusDailyRerolls);
        Assert.Equal(weekly, s.BonusWeeklyRerolls);
    }
}
