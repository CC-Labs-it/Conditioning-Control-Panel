using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class SyncProgressMergeTests
{
    [Fact]
    public void CompanionMergeUsesLevelThenLifetimeXpAndPreservesOtherCompanions()
    {
        var s = new AppSettings { TotalConditioningMinutes = 10,
            CompanionProgressData = new Dictionary<int, CompanionProgress> {
                [0] = new() { Level = 3, TotalXPEarned = 900 },
                [1] = new() { Level = 2, TotalXPEarned = 20 } } };
        var baselines = 0;
        var sync = new SyncPush(() => null, () => false) { LifetimeLifted = () => baselines++ };
        sync.AdoptFromResponse(s, JObject.Parse("""{"total_conditioning_minutes":15,"companion_progress":{"0":{"Level":2,"TotalXPEarned":9999},"1":{"Level":2,"TotalXPEarned":30},"2":{"Level":1},"invalid":{"Level":9}}}"""));
        Assert.Equal(15, s.TotalConditioningMinutes);
        Assert.Equal(1, baselines);
        Assert.Equal(900, s.CompanionProgressData[0].TotalXPEarned);
        Assert.Equal(30, s.CompanionProgressData[1].TotalXPEarned);
        Assert.Equal(1, s.CompanionProgressData[2].Level);
        Assert.Equal(3, s.CompanionProgressData.Count);
        sync.AdoptFromResponse(s, JObject.Parse("""{"total_conditioning_minutes":1,"companion_progress":{"0":{"Level":4,"TotalXPEarned":1},"1":null}}"""));
        Assert.Equal(15, s.TotalConditioningMinutes);
        Assert.Equal(1, baselines);
        Assert.Equal(4, s.CompanionProgressData[0].Level);
        Assert.Equal(30, s.CompanionProgressData[1].TotalXPEarned);
    }

    [Fact]
    public void MalformedCompanionRecordDoesNotAbortTheAdopt()
    {
        var s = new AppSettings { TotalConditioningMinutes = 10, CompanionProgressData = new Dictionary<int, CompanionProgress>() };
        var sync = new SyncPush(() => null, () => false);
        var known = sync.AdoptFromResponse(s, JObject.Parse("""{"skill_points":3,"unlocked_skills":[],"total_conditioning_minutes":15,"companion_progress":{"0":{"Level":"not-a-number"}}}"""));
        Assert.True(known);
        Assert.Equal(15, s.TotalConditioningMinutes);
        Assert.False(s.CompanionProgressData.ContainsKey(0));
    }

}
