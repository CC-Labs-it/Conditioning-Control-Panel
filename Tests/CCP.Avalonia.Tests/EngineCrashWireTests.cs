using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class EngineCrashWireTests
{
    [Fact]
    public void Launch_fires_once_only_for_a_consumed_sentinel_and_exit_clears()
    {
        var path = Path.Combine(CorePaths.UserData, "logs", "engine_session.active");
        EngineCrashSentinel.Clear();
        int fires = 0;
        try
        {
            App.RecoverEngineCrash(() => fires++);
            Assert.Equal(0, fires);
            EngineCrashSentinel.Mark("crash");
            App.RecoverEngineCrash(() => fires++);
            App.RecoverEngineCrash(() => fires++);
            Assert.Equal(1, fires);
            EngineCrashSentinel.Mark("running");
            App.ClearEngineCrashOnExit();
            Assert.False(File.Exists(path));
        }
        finally { EngineCrashSentinel.Clear(); }
    }
}
