using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PanicAftermathPortTests
{
    [Fact]
    public void RemotePanicArmsQuietButRemoteDisconnectDoesNot()
    {
        PanicAftermath.EndQuiet();
        try
        {
            RemoteCommands.StopEffects(force: false);
            Assert.False(PanicAftermath.TriggersSilenced());
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.True(PanicAftermath.TriggersSilenced());
            Assert.True(PanicAftermath.HoldsEngineRestart);
        }
        finally { PanicAftermath.EndQuiet(); }
    }
}
