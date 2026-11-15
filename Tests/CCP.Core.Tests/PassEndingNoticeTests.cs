using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Startup.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The "your pass ends in N days" notice: reading the pass off the profile, and when
/// the row is owed (the last three days, and only when nothing outlasts the pass).</summary>
public class PassEndingNoticeTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_ReadsThePassEnd()
    {
        var r = ProfileExtras.Parse(JObject.Parse(
            "{\"crypto_pass\":{\"tier\":2,\"prime_until\":\"2026-11-13T12:00:00.000Z\",\"basic_until\":null,\"ends_at\":\"2026-11-13T12:00:00.000Z\",\"days_left\":2}}"))!;
        Assert.True(r.PassSent);
        Assert.Equal(new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc), r.PassEndsUtc);
    }

    [Fact]
    public void Parse_NullPassIsSentAndEmpty_AnOlderServerIsNotSent()
    {
        var none = ProfileExtras.Parse(JObject.Parse("{\"crypto_pass\":null}"))!;
        Assert.True(none.PassSent);
        Assert.Null(none.PassEndsUtc);
        Assert.False(ProfileExtras.Parse(JObject.Parse("{\"paid_until\":null}"))!.PassSent);
    }

    [Fact]
    public void Apply_StoresAndClears_OnlyWhenTheServerSaidSo()
    {
        var s = new AppSettings();
        var ends = Now.AddDays(2);
        Assert.True(ProfileExtras.Apply(s, new ProfileExtrasReading(null, false, null, null) { PassSent = true, PassEndsUtc = ends }));
        Assert.Equal(ends, s.PassEndsUtc);
        Assert.False(ProfileExtras.Apply(s, new ProfileExtrasReading(null, false, null, null)));   // an older server: kept
        Assert.Equal(ends, s.PassEndsUtc);
        Assert.True(ProfileExtras.Apply(s, new ProfileExtrasReading(null, false, null, null) { PassSent = true }));
        Assert.Null(s.PassEndsUtc);
    }

    [Theory]
    [InlineData(72, 3)]
    [InlineData(49, 3)]
    [InlineData(48, 2)]
    [InlineData(25, 2)]
    [InlineData(24, 1)]
    [InlineData(1, 1)]
    public void Owed_InTheLastThreeDays(int hoursLeft, int days)
    {
        var ends = Now.AddHours(hoursLeft);
        Assert.Equal(days, ProfileExtras.PassEndingDays(ends, ends, Now));
    }

    [Fact]
    public void NotOwed_EarlyOrOver()
    {
        Assert.Equal(0, ProfileExtras.PassEndingDays(Now.AddHours(73), Now.AddHours(73), Now));
        Assert.Equal(0, ProfileExtras.PassEndingDays(Now.AddHours(-1), Now.AddHours(-1), Now));
        Assert.Equal(0, ProfileExtras.PassEndingDays(null, Now.AddDays(1), Now));
    }

    [Fact]
    public void NotOwed_WhenASubscriptionOutlastsThePass()
    {
        var ends = Now.AddDays(2);
        Assert.Equal(0, ProfileExtras.PassEndingDays(ends, null, Now));             // a pledge with no end date
        Assert.Equal(0, ProfileExtras.PassEndingDays(ends, Now.AddDays(20), Now));  // a paid period past the pass
    }

    [Fact]
    public void Owed_WhenPrimeTurnsIntoBasicLater_CountsToTheEndOfThePass()
    {
        // Prime until tomorrow, Basic after it for two more days: paid_until is the Prime date.
        Assert.Equal(3, ProfileExtras.PassEndingDays(Now.AddDays(3), Now.AddDays(1), Now));
    }
}
