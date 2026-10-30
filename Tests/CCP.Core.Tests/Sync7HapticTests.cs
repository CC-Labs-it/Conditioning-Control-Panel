using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Core.Tests;

public sealed class Sync7HapticTests
{
    [Fact]
    public void TestWindowAdmitsOnlyTestPulsesWhileMasterIsOff()
    {
        Assert.True(HapticMixer.AdmitsPulse(false, true, true));
        Assert.False(HapticMixer.AdmitsPulse(false, true, false));
        Assert.True(HapticMixer.AdmitsPulse(true, true, false));
        Assert.False(HapticMixer.AdmitsPulse(true, false, true));
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelledAllToyPreviewReportsNoPlayback()
    {
        var previous = CoreEntitlement.HasPremiumProvider;
        CoreEntitlement.HasPremiumProvider = () => true;
        try
        {
            var settings = new HapticSettings { Enabled = false };
            using var service = new HapticService(settings);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.False(await service.TestPatternAsync(0.5, 1500, VibrationMode.Constant, cancel.Token));
        }
        finally { CoreEntitlement.HasPremiumProvider = previous; }
    }
}
