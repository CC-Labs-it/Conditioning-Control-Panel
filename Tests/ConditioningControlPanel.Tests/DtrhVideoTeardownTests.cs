using System;
using System.IO;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1470: "video stuck on the screen after the fall ended". A clip on the tube's
/// spotlight when a descent ended was parked, not ended: its countdown only ticks while the
/// conveyor runs, so it froze in front of the camera over the recap and the hub. The stage rule
/// itself is pure and node tested (dtrh/tests/video-stage.test.mjs); these pin the host's half
/// and the wiring that makes the rule reach the screen.
/// </summary>
public class DtrhVideoTeardownTests
{
    [Fact]
    public void TheHostStillPlaysTheNativeWhisper()
    {
        Assert.True(DtrhNativePayloadRule.Accepts("audio"));
        Assert.True(DtrhNativePayloadRule.Accepts("AUDIO"));
    }

    /// <summary>A native video from the host carries no run token, so no run exit, hub return
    /// or host close could call it back. The page renders its video card in-world.</summary>
    [Theory]
    [InlineData("video")]
    [InlineData("Video")]
    public void TheHostOpensNoNativeVideo(string kind)
        => Assert.False(DtrhNativePayloadRule.Accepts(kind));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("flash")]
    [InlineData("overlay")]
    public void EveryOtherKindIsInWorld(string? kind)
        => Assert.False(DtrhNativePayloadRule.Accepts(kind));

    [Fact]
    public void EveryStandDownClosesTheStage()
    {
        // setRunActive, the visibility handler and the pause all go through syncSpawner, and
        // chaosRun's endRun and returnToWarren both call setRunActive(false).
        var body = FunctionBody(Web("engine", "scene.js"), "function syncSpawner()");
        Assert.Contains("spawner.setStageClosed(stageClosedFor(!!game, runActive))", body);
    }

    [Fact]
    public void ClosingTheStageRetiresTheClipOnIt()
    {
        var body = FunctionBody(Web("engine", "spawner.js"), "function setStageClosed(on)");
        Assert.Contains("stage.setClosed(on, !!spotlight)", body);
        Assert.Contains("spotlight.clipLeft = 0", body);
    }

    [Fact]
    public void AClipAskedForBeforeTheRunEndedIsRefusedWhenItLands()
    {
        var body = FunctionBody(Web("engine", "spawner.js"), "async function forceSpotlight(");
        var await_ = body.IndexOf("await entry.acquire()", StringComparison.Ordinal);
        Assert.True(await_ > 0, "forceSpotlight no longer awaits an acquire - update this test");
        Assert.Contains("!stage.mayOpen(ticket)", body.Substring(await_));
    }

    private static string Web(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var root = Path.Combine(dir!.FullName, "Assets", "web", "dtrh");
        return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
    }

    /// <summary>The text from <paramref name="signature"/> to the next blank-line-separated
    /// top-level function, enough to scope a Contains to one function.</summary>
    private static string FunctionBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was renamed - update this test with it");
        var end = source.IndexOf("\n  function ", start + signature.Length, StringComparison.Ordinal);
        var endAsync = source.IndexOf("\n  async function ", start + signature.Length, StringComparison.Ordinal);
        if (endAsync > 0 && (end < 0 || endAsync < end)) end = endAsync;
        return end > 0 ? source.Substring(start, end - start) : source.Substring(start);
    }
}
