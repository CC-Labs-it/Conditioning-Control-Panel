using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PanicProgramStartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmittedLocalProgramStartReleasesQuietOnlyWhenStartingEngine(bool engineRunning)
    {
        try
        {
            PanicAftermath.ArmQuiet();
            PanicAftermath.UserProgramStart(engineRunning);
            Assert.Equal(engineRunning, PanicAftermath.TriggersSilenced());
            Assert.Equal(engineRunning, PanicAftermath.HoldsEngineRestart);
        }
        finally { PanicAftermath.EndQuiet(); }
    }
}
