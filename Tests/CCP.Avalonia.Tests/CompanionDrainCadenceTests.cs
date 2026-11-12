using System;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CompanionDrainCadenceTests
{
    [Fact]
    public void PerkAndCompanionChangesRestartOrStopDrain() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEnv = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var service = new SettingsService();
        var oldProvider = CoreSettings.ServiceProvider;
        var wasStarted = CompanionHead.Started;
        try
        {
            if (wasStarted) CompanionHead.Stop();
            CoreSettings.ServiceProvider = () => service;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "1");
            var s = CoreSettings.Current;
            s.PlayerLevel = 1000;
            s.CompanionPerk = CompanionBonusType.PinkFilterBonus;
            CompanionHead.Start();
            Assert.Null(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var first = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var second = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.NotSame(first, second);
            var next = CompanionCore.ActiveCompanion == CompanionId.CultBunny ? CompanionId.OGBambiSprite : CompanionId.CultBunny;
            CompanionCore.SwitchCompanion(next);
            Assert.NotSame(second, CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.PinkFilterBonus));
            Assert.Null(CompanionHead.DrainTimer);
        }
        finally
        {
            CompanionHead.Stop();
            CoreSettings.ServiceProvider = oldProvider;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", oldEnv);
            if (wasStarted) CompanionHead.Start();
        }
    });
}
