using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Core.Tests;

public sealed class Sync7HapticTests
{
    private sealed class BlockedUiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { }
    }

    [Fact]
    public void AllToyPreviewCancellationCompletesWithoutUiDispatcher()
    {
        var previousPremium = CoreEntitlement.HasPremiumProvider;
        var previousContext = SynchronizationContext.Current;
        CoreEntitlement.HasPremiumProvider = () => true;
        try
        {
            using var service = new HapticService(new HapticSettings { Enabled = false });
            using var cancel = new CancellationTokenSource();
            SynchronizationContext.SetSynchronizationContext(new BlockedUiContext());
            var preview = service.TestPatternAsync(0.5, 8000, VibrationMode.Constant, cancel.Token);
            Assert.False(preview.IsCompleted);
            cancel.Cancel();
            // Hiding the page cancels then waits on the UI thread. Core must finish independently.
            Assert.True(preview.Wait(3000));
            Assert.False(preview.Result);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            CoreEntitlement.HasPremiumProvider = previousPremium;
        }
    }

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
