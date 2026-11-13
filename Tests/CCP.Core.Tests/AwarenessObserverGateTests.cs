using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AwarenessObserverGateTests
{
    [Fact]
    public void RequiresEntitlementAndEveryConsentAndSwitch()
    {
        var settings = new AppSettings
        {
            UseAwarenessV2 = true,
            AwarenessModeEnabled = true,
            AwarenessConsentGiven = true,
            AwarenessConsentShownV2 = true
        };
        Assert.True(AwarenessObserverPolicy.IsEnabled(settings, true));
        Assert.False(AwarenessObserverPolicy.IsEnabled(null, true));
        Assert.False(AwarenessObserverPolicy.IsEnabled(settings, false));
        settings.UseAwarenessV2 = false;
        Assert.False(AwarenessObserverPolicy.IsEnabled(settings, true));
        settings.UseAwarenessV2 = true;
        settings.AwarenessModeEnabled = false;
        Assert.False(AwarenessObserverPolicy.IsEnabled(settings, true));
        settings.AwarenessModeEnabled = true;
        settings.AwarenessConsentGiven = false;
        Assert.False(AwarenessObserverPolicy.IsEnabled(settings, true));
        settings.AwarenessConsentGiven = true;
        settings.AwarenessConsentShownV2 = false;
        Assert.False(AwarenessObserverPolicy.IsEnabled(settings, true));
    }
}
