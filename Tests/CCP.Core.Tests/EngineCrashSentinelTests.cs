using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// Restack b09: the start mark and stop clear live in the shell's engine hooks (MainShellWindow.OnEngineStarted /
// OnEngineStopped, WPF MainWindow.StartStop.cs:426,582), not in CoreEngine; only the consume rule is Core.
[Collection(SessionStatics.Name)]
public sealed class EngineCrashSentinelTests
{
    [Fact]
    public void Leftover_is_consumed_once()
    {
        EngineCrashSentinel.Mark("leftover");
        try
        {
            Assert.True(EngineCrashSentinel.ConsumeAndReport(null));
            Assert.False(EngineCrashSentinel.ConsumeAndReport(null));
        }
        finally { EngineCrashSentinel.Clear(); }
    }
}
