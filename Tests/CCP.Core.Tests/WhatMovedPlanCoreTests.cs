using System.Linq;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WhatMovedPlanCoreTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(-1, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(3, true, false)]
    public void UpgradeOfferIsUnspentAndFreshInstallsNeverOffer(int shown, bool fresh, bool expected)
        => Assert.Equal(expected, WhatMovedPlan.ShouldOffer(shown, fresh));

    [Fact]
    public void SharedRowsKeepTheirOrderAndTargetGlowKeys()
    {
        Assert.Equal(new[] { "premium", "lobby", "monitors", "tabs", "games" }, WhatMovedPlan.Rows.Select(r => r.Id));
        Assert.Equal(new[] { "account", "availablesubjects", "monitors", "studio", "play" }, WhatMovedPlan.Rows.Select(r => r.GlowKey));
        foreach (var row in WhatMovedPlan.Rows)
        {
            Assert.Equal(row.Text, row.ToString());
            if (row.SettingsSection != null) Assert.Equal("appsettings", row.Tab);
        }
    }
}
