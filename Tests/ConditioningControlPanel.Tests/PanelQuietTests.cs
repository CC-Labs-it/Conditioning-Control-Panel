using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The panel stands down while the show runs: overlay frames are recorded on the UI dispatcher, so a
/// panel ambient loop is paid out of overlay frame time (tier-2 report on 7.3.2). The show's own
/// surfaces and the launcher keep their motion: they ask the level-only gate.
/// </summary>
public class PanelQuietTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // Auto performance off is the user's opt-out
    [InlineData(false, false, false)]
    public void Quiet_only_while_the_show_runs_with_auto_performance(bool show, bool auto, bool expected)
        => Assert.Equal(expected, MotionFx.QuietRule(show, auto));

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    /// <summary>What the show draws, and the launcher, must never ask the panel's gate: it is closed
    /// exactly while they are on screen.</summary>
    [Theory]
    [InlineData("Services/Compositor")]
    [InlineData("Services/BubbleService.cs")]
    [InlineData("Windows/Launcher")]
    [InlineData("AvatarTube")]
    [InlineData("Windows/RemoteHud")]
    public void Show_surfaces_ask_the_level_only_gate(string rel)
    {
        var path = Path.Combine(AppDir(), rel.Replace('/', Path.DirectorySeparatorChar));
        var files = File.Exists(path) ? new[] { path } : Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
        var bad = files.Where(f =>
        {
            var text = File.ReadAllText(f);
            return text.Contains("MotionFx.AllowAmbientLoops ") || text.Contains("MotionFx.AllowAmbientLoops)")
                || text.Contains("MotionFx.AllowAmbientLoops;") || text.Contains("MotionFx.AllowParticles");
        }).Select(Path.GetFileName).ToArray();
        Assert.True(bad.Length == 0, "asks the panel gate: " + string.Join(", ", bad));
    }
}
