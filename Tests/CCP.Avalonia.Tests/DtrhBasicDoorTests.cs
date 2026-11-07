using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DtrhBasicDoorTests
{
    [Theory]
    [InlineData(false, false, null, false)]
    [InlineData(false, false, "remote", false)]
    [InlineData(false, false, "dtrh", true)]
    [InlineData(true, false, null, true)]
    [InlineData(true, true, "dtrh", true)]
    public void GateLauncherAndEmiUseBasicOrTheDtrhDrop(bool premium, bool lab, string? freeKey, bool allowed)
    {
        var old = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider,
            CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler);
        TierVerdict? denied = null;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.HasLabProvider = () => lab;
            CoreEntitlement.IsFreeTodayProvider = key => key == freeKey;
            CoreEntitlement.ShowDeniedHandler = verdict => denied = verdict;
            Assert.Equal(!allowed, GameWindow.DtrhLocked());
            Assert.Equal(!allowed, LauncherWindow.Destinations["dtrh"].Locked());
            var target = Assert.IsType<EmiTarget>(EmiTargets.Find("dtrh"));
            Assert.True(target.Available);
            Assert.Equal(!allowed, target.Locked);
            Assert.Equal(allowed, GameWindow.DtrhGate());
            if (allowed) Assert.Null(denied);
            else
            {
                Assert.Equal(PatreonTier.Level1, denied!.Value.Required);
                denied = null;
                target.Open();
                Assert.Equal(PatreonTier.Level1, denied!.Value.Required);
            }
        }
        finally
        {
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider,
                CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler) = old;
        }
    }
}
