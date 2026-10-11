using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1420: with the haptics master toggle off, "Test this toy" on toy 1 let a bubble pop
/// buzz toy 2. The test window waived the master toggle for the whole mixer, so any live event
/// that landed inside it rode out to every other toy, then went silent again when it closed.
///
/// The window is for the test's own pulses. These pin that rule.
/// </summary>
public class HapticTestWindowTests
{
    [Fact]
    public void MasterOn_EveryPulsePlays()
    {
        Assert.True(HapticMixer.AdmitsPulse(masterOn: true, gateOpen: true, isTest: false));
        Assert.True(HapticMixer.AdmitsPulse(masterOn: true, gateOpen: true, isTest: true));
    }

    [Fact]
    public void MasterOff_TestWindowAdmitsOnlyTheTest()
    {
        // gateOpen with the master off means a test window is running.
        Assert.True(HapticMixer.AdmitsPulse(masterOn: false, gateOpen: true, isTest: true));
        Assert.False(HapticMixer.AdmitsPulse(masterOn: false, gateOpen: true, isTest: false));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ClosedGate_NothingPlays(bool masterOn, bool isTest)
        => Assert.False(HapticMixer.AdmitsPulse(masterOn, gateOpen: false, isTest));
}
