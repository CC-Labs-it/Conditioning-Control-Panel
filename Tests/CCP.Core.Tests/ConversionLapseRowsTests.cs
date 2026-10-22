using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup.Conversion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Lapse rows (brief C9, proposals 4a / 4b): a lower server tier is remembered, the grace end is
/// warned about at 7 / 3 / 1 days and once on the day it closes, and each row is stamped so it
/// posts once per lapse.
/// </summary>
public class ConversionLapseRowsTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ServerTierSeen_IsKeptOnLower_ClearedOnSameOrRise_UntouchedOnNoSignal()
    {
        Assert.Equal(1, EntitlementTierRule.ServerTierSeenAfter(EntitlementVerdict.Lower, 1, null));
        Assert.Null(EntitlementTierRule.ServerTierSeenAfter(EntitlementVerdict.Same, 2, 1));
        Assert.Null(EntitlementTierRule.ServerTierSeenAfter(EntitlementVerdict.Rise, 2, 0));
        Assert.Equal(0, EntitlementTierRule.ServerTierSeenAfter(EntitlementVerdict.NoSignal, null, 0));
    }

    [Theory]
    [InlineData(10.0, LapseRow.None, 0)]
    [InlineData(7.0, LapseRow.Warn7, 7)]
    [InlineData(6.5, LapseRow.Warn7, 7)]
    [InlineData(3.9, LapseRow.Warn7, 4)]
    [InlineData(3.0, LapseRow.Warn3, 3)]
    [InlineData(1.5, LapseRow.Warn3, 2)]
    [InlineData(0.5, LapseRow.Warn1, 1)]
    [InlineData(-2.0, LapseRow.Closed, 0)]
    [InlineData(-31.0, LapseRow.None, 0)]
    public void Prime_Lapse_BucketsByDaysLeft(double daysLeft, LapseRow row, int days)
    {
        var r = ConversionRules.DecideLapse(2, 1, Now.AddDays(14), Now.AddDays(daysLeft), Now, liveCoversIt: false);
        Assert.Equal(row, r.Row);
        if (row == LapseRow.None) return;
        Assert.Equal(2, r.Tier);
        Assert.Equal(days, r.DaysLeft);
    }

    [Fact]
    public void Basic_Lapse_ReadsThePremiumStamp()
    {
        var r = ConversionRules.DecideLapse(1, 0, Now.AddDays(2), null, Now, liveCoversIt: false);
        Assert.Equal(LapseRow.Warn3, r.Row);
        Assert.Equal(1, r.Tier);
        Assert.Equal(ConversionRules.KeyLapseWarn3, r.Key);
    }

    [Fact]
    public void Prime_FallsBackToThePremiumStamp_WhenTheLabStampIsMissing()
    {
        var r = ConversionRules.DecideLapse(2, 0, Now.AddDays(1), null, Now, liveCoversIt: false);
        Assert.Equal(LapseRow.Warn1, r.Row);
        Assert.Equal(2, r.Tier);
    }

    [Theory]
    [InlineData(null)]   // the server never reported lower: a stale stamp alone never warns
    [InlineData(2)]      // server agrees
    public void NoRow_WithoutALowerServerReading(int? seen)
        => Assert.Equal(LapseRow.None, ConversionRules.DecideLapse(2, seen, Now.AddDays(2), Now.AddDays(2), Now, false).Row);

    [Fact]
    public void NoRow_WhenSomethingLiveStillCoversIt()
        => Assert.Equal(LapseRow.None, ConversionRules.DecideLapse(2, 1, Now.AddDays(2), Now.AddDays(2), Now, liveCoversIt: true).Row);

    [Fact]
    public void NoRow_WithoutAGraceStamp()
        => Assert.Equal(LapseRow.None, ConversionRules.DecideLapse(1, 0, null, null, Now, false).Row);

    [Fact]
    public void Stamps_PostOncePerValue()
    {
        var stamps = new Dictionary<string, string>();
        Assert.True(ConversionRules.Owed(stamps, "lapse_warn_3", "2026-10-15"));
        Assert.True(ConversionRules.Stamp(stamps, "lapse_warn_3", "2026-10-15"));
        Assert.False(ConversionRules.Owed(stamps, "lapse_warn_3", "2026-10-15"));
        Assert.False(ConversionRules.Stamp(stamps, "lapse_warn_3", "2026-10-15"));
        // A later lapse (another grace end) warns again.
        Assert.True(ConversionRules.Owed(stamps, "lapse_warn_3", "2026-12-01"));
        Assert.False(ConversionRules.Owed(stamps, "", "x"));
    }

    [Fact]
    public void LapseReading_StampIsTheGraceEndDate()
    {
        var r = ConversionRules.DecideLapse(2, 1, null, new DateTime(2026, 10, 16, 3, 0, 0, DateTimeKind.Utc), Now, false);
        Assert.Equal("2026-10-16", r.Stamp);
    }

    [Fact]
    public void PlanUrls_AreTheTierCheckouts_NeverTheBareProfile()
    {
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27604786", ConversionRules.PlanUrl(1));
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27681505", ConversionRules.PlanUrl(2));
        Assert.Equal("basic", ConversionRules.PlanWord(1));
        Assert.Equal("prime", ConversionRules.PlanWord(2));
    }

    [Fact]
    public void NewSettings_RoundTripThroughJson()
    {
        var s = new AppSettings { ServerTierSeen = 1 };
        s.InboxStamps["lapse_closed"] = "2026-10-16";
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.Equal(1, back.ServerTierSeen);
        Assert.Equal("2026-10-16", back.InboxStamps["lapse_closed"]);
        Assert.Null(new AppSettings().ServerTierSeen);
    }

}
