using System.IO;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1620: with the engine off the companion giggled about once a minute. The screen-reading
/// keyword trigger's avatar comment (KeywordTriggerService.ShowAvatarLine, logged as "Awareness
/// comment") called GigglePriority without the unprompted flag, so CompanionIdleVoice never held its
/// giggle. The comment is a sensing line: it keeps its bubble, and its sound follows the idle rule.
/// </summary>
public class KeywordCommentIdleVoiceTests
{
    [Fact]
    public void The_keyword_comment_hands_the_unprompted_flag_to_the_giggle()
    {
        var svc = Src("ConditioningControlPanel", "Services", "KeywordTriggerService.cs");
        Assert.Contains("avatar.GigglePriority(line, playSound: true, aiGenerated: aiGenerated, unprompted: true);", svc);
        Assert.DoesNotContain("avatar.GigglePriority(line, playSound: true, aiGenerated: aiGenerated);", svc);
    }

    [Fact]
    public void GigglePriority_carries_the_flag_to_the_idle_hold()
    {
        var tube = Src("ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Speech.cs");
        Assert.Contains("unprompted: unprompted, soundCaller: who", tube);
        Assert.Contains("bool heldIdle = !Services.Companion.CompanionIdleVoice.ShouldVoiceNow(unprompted);", tube);
    }

    [Theory]
    // engine, session running, session paused, opted in, panic quiet => the comment's giggle plays
    [InlineData(false, false, false, false, false, false)] // the bug: engine off, nothing running
    [InlineData(true, false, false, false, false, true)]   // engine on: the comment still giggles
    [InlineData(false, true, false, false, false, true)]   // a session running counts as on
    [InlineData(false, true, true, false, false, false)]   // a panic-paused session does not
    [InlineData(false, false, false, true, false, true)]   // "speak while idle" opted in
    [InlineData(true, false, false, false, true, false)]   // the post-panic quiet holds it even with the engine on
    public void An_unprompted_comment_is_quiet_with_the_engine_off_and_voiced_with_it_on(
        bool engine, bool session, bool paused, bool optedIn, bool panicQuiet, bool voiced)
    {
        Assert.Equal(voiced, CompanionIdleVoice.ShouldVoice(unprompted: true, engine, session, paused, optedIn, panicQuiet));
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
