using ConditioningControlPanel.Services.Autonomy;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace CCP.Core.Tests;
public class TakeoverCommentPortTests
{
    [Theory]
    [InlineData("hello", " HELLO ", true)]
    [InlineData("hello", "another line", false)]
    [InlineData(null, "hello", false)]
    [InlineData(" ", " ", false)]
    public void RepeatIsTrimmedAndCaseInsensitive(string? last, string? next, bool expected)
        => Assert.Equal(expected, TakeoverComment.IsRepeat(last, next));

    [Fact]
    public void HeadRoutesThroughAmbientBrainAndDropsRepeatLines()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "CCP.Avalonia"))) dir = dir.Parent;
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "CCP.Avalonia", "Views", "Windows", "MainShellWindow.Autonomy.cs"));
        Assert.Contains("BrainAdapter.ShouldRouteAmbient(App.Brain", source);
        Assert.Contains("BrainAdapter.ReactAsync(App.Brain!", source);
        Assert.Contains("FrameFormatter.TakeoverCommentEvent()", source);
        Assert.Contains("TakeoverComment.Prompt", source);
        Assert.Contains("TakeoverComment.IsRepeat(_lastAICommentLine, text)", source);
    }

    [Fact]
    public void UnpromptedCommentNeverCastsTheUserAsAttentionSeeking()
    {
        Assert.DoesNotContain("to get attention", TakeoverComment.Prompt);
        Assert.Contains("did nothing to ask for it", TakeoverComment.Prompt);
        Assert.Contains("asked for nothing", FrameFormatter.TakeoverCommentEvent());
    }
}
