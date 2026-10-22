using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Down the Rabbit Hole page learns the app's Motion setting from <c>init.motionLevel</c>.
/// These pin the three wire words the page's <c>shared/motion.js</c> accepts.
/// </summary>
[Collection(CCP.Core.Tests.SessionStatics.Name)]   // Current() reads the process-wide CoreSettings
public class DtrhMotionWireTests
{
    [Theory]
    [InlineData(MotionLevel.Full, "full")]
    [InlineData(MotionLevel.Reduced, "reduced")]
    [InlineData(MotionLevel.Off, "off")]
    public void EachSettingHasItsWireWord(MotionLevel setting, string word)
        => Assert.Equal(word, DtrhMotionWire.For(setting));

    /// <summary>A value the page does not know would make it fall back to the OS, so an
    /// out-of-range setting reads as full instead.</summary>
    [Fact]
    public void AnUnknownSettingReadsAsFull()
        => Assert.Equal("full", DtrhMotionWire.For((MotionLevel)99));

    /// <summary>Current() carries the player's own setting (CoreSettings, which WPF seeds with
    /// App.Settings), not a constant.</summary>
    [Fact]
    public void CurrentReadsThePlayersSetting()
    {
        var old = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Assert.Equal("off", DtrhMotionWire.Current());
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            Assert.Equal("reduced", DtrhMotionWire.Current());
        }
        finally { CoreSettings.Current.MotionLevel = old; }
    }
}
