using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The licence card (#2503) rides the startup ladder's modal pump, which never waits for a session
/// or a Lockdown, and the server flag can flip mid-run. The card must ask the hold itself, both
/// when it is offered and when its turn comes.
/// </summary>
public class OnlineLicenceCardHoldTests
{
    [Fact]
    public void TheCardIsNeverQueuedWhileAHoldIsOn()
    {
        var src = Source("ConditioningControlPanel", "MainWindow", "MainWindow.OnlineMeter.cs");
        Assert.Contains("if (!st.ShowOfficialNote || _onlineLicenceCardQueued || FriendsLanding.Holding()) return;", src);
    }

    [Fact]
    public void ACardQueuedBeforeASessionStepsAsideAndIsOfferedAgain()
    {
        var src = Source("ConditioningControlPanel", "MainWindow", "MainWindow.OnlineMeter.cs");
        Assert.Contains("if (FriendsLanding.Holding()) { _onlineLicenceCardQueued = false; return; }", src);
        // The recheck runs before the dialog opens.
        Assert.True(src.IndexOf("if (FriendsLanding.Holding()) { _onlineLicenceCardQueued = false; return; }", StringComparison.Ordinal)
            < src.IndexOf("new OnlineLicenceCard(now.Plan)", StringComparison.Ordinal));
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
