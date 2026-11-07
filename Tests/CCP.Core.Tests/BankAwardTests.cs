using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class BankAwardTests
{
    [Theory]
    [InlineData("Quest", XPSource.Quest, false)]
    [InlineData("unknown", XPSource.Other, true)]
    public void PresentationGetsAdjustedXpAndLevelResultWhileQuestsKeepTheBaseAmount(string source, XPSource expectedSource, bool levels)
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "bank-core-test";
        s.PlayerXP = 0;
        s.PlayerLevel = 10;
        s.DescentMigrationCompleted = true;
        double factor = ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(s);
        double amount = levels ? XpCurve.GetXPForLevel(10, XpCurve.EpochOf(s)) / factor + 1 : 5;
        var events = new List<string>();
        ProgressionBank.XpAward? observed = null;
        double baseAward = 0;
        Action<ProgressionBank.XpAward> display = a => { observed = a; events.Add("display"); };
        Action<double, string> quest = (a, _) => { baseAward = a; events.Add("quest"); };
        ProgressionBank.XPAwarded += display;
        ProgressionBank.Awarded += quest;
        try
        {
            ProgressionBank.Add(amount, source);
            Assert.NotNull(observed);
            Assert.Equal(amount * factor, observed!.Amount, 8);
            Assert.Equal(expectedSource, observed.Source);
            Assert.Equal(levels, observed.LeveledUp);
            Assert.Equal(amount, baseAward);
            Assert.Equal(new[] { "quest", "display" }, events);
        }
        finally
        {
            ProgressionBank.XPAwarded -= display;
            ProgressionBank.Awarded -= quest;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    }

    // WPF ProgressionService.cs:120,125-126,240-249: decoration cannot break the award.
    [Fact]
    public void ThrowingPresentationCannotBreakTheAwardOrSkipQuestCredit()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "bank-core-test";
        s.PlayerXP = 0;
        s.PlayerLevel = 10;
        double baseAward = 0;
        double expectedXp = 5 * ConditioningControlPanel.Models.PinkRushRules.XpFactor(s)
            * ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(s);
        bool displayCalled = false;
        Action<double, string> quest = (amount, _) => baseAward = amount;
        Action<ProgressionBank.XpAward> display = _ =>
        {
            displayCalled = true;
            throw new InvalidOperationException("Test presentation failure");
        };
        ProgressionBank.Awarded += quest;
        ProgressionBank.XPAwarded += display;
        try
        {
            var error = Record.Exception(() => ProgressionBank.Add(5, "Quest"));
            Assert.Null(error);
            Assert.True(displayCalled);
            Assert.Equal(5, baseAward);
            Assert.Equal(expectedXp, s.PlayerXP);
        }
        finally
        {
            ProgressionBank.Awarded -= quest;
            ProgressionBank.XPAwarded -= display;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    }
}
