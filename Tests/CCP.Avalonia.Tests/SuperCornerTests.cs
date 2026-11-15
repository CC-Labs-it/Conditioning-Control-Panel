using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SuperCornerTests
{
    [Theory]
    [InlineData("FlashImages", SuperEffect.FlickerDeck)]
    [InlineData("Video", SuperEffect.LightsDown)]
    [InlineData("Subliminals", SuperEffect.Afterglow)]
    [InlineData("BouncingText", SuperEffect.Scrawl)]
    [InlineData("SpiralOverlay", SuperEffect.Vortex)]
    [InlineData("PinkFilter", SuperEffect.Creep)]
    [InlineData("BrainDrain", SuperEffect.Undertow)]
    [InlineData("BubblePop", SuperEffect.InnerBloom)]
    public void HelpMapsToEffect(string help, SuperEffect effect) => Assert.Equal(effect, SuperDoor.ForHelp(help));

    [Fact]
    public void UnknownHelpHasNoEffect()
    {
        Assert.Null(SuperDoor.ForHelp(null));
        Assert.Null(SuperDoor.ForHelp("LockCard"));
    }

    [Fact]
    public void TwistUsesTheEffectDescription()
    {
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("super_twist_scrawl"), SuperDoor.Twist(SuperEffect.Scrawl));
        Assert.NotEqual(SuperDoor.Twist(SuperEffect.Creep), SuperDoor.Twist(SuperEffect.Scrawl));
    }

    [Fact]
    public void PressRefusesFreeAndFlipsPaid() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var premium = CoreEntitlement.HasPremiumProvider;
        var denied = CoreEntitlement.ShowDeniedHandler;
        var trying = SuperAccess.TryingProvider;
        var overrideValue = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var service = new SettingsService();
        int refusals = 0, changes = 0;
        void Changed(SuperEffect e) { if (e == SuperEffect.Scrawl) changes++; }
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            SuperAccess.TryingProvider = () => null;
            SuperAccess.Changed += Changed;
            Assert.False(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.Equal(1, refusals);
            Assert.Empty(service.Current.SuperModes);
            Assert.Equal(0, changes);
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.True(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.True(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.Equal(2, changes);
        }
        finally
        {
            SuperAccess.Changed -= Changed;
            SuperAccess.TryingProvider = trying;
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.ShowDeniedHandler = denied;
            CoreSettings.ServiceProvider = provider;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", overrideValue);
            service.SealForReset();
        }
    });
}
