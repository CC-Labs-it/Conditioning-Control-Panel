using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1569 #1573 #1575 #1576: the "random audio while idle" was the companion's unprompted
/// ask card (CompanionAskService's 60 s pacing timer). Its giggle went through GigglePriority without
/// the unprompted flag, so CompanionIdleVoice never held it with the engine off. The card must carry
/// the flag to the giggle; cards the player asked for stay voiced.
/// </summary>
public class AskCardIdleVoiceTests
{
    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void An_unprompted_card_hands_its_flag_to_the_giggle()
    {
        var tube = Src("ConditioningControlPanel", "AvatarTube", "AvatarTubeWindow.Asks.cs");
        Assert.Contains("internal void ShowAskCard(AskCard card, bool unprompted = false)", tube);
        Assert.Contains("GigglePriority(card.Question, playSound: true, aiGenerated: false, unprompted: unprompted);", tube);

        var svc = Src("ConditioningControlPanel", "Services", "Companion", "Asks", "CompanionAskService.cs");
        Assert.Contains("ShowAskCard(card, unprompted)", svc);
        Assert.Contains("if (card != null) Show(card, unprompted: true);", svc);   // the timer's own card
        Assert.Contains("if (card != null) Show(card, unprompted: false);", svc);  // ShowNow: the player asked
    }

    [Fact]
    public void Deeper_and_a_running_Takeover_action_count_as_busy()
    {
        var svc = Src("ConditioningControlPanel", "Services", "Companion", "Asks", "CompanionAskService.cs");
        Assert.Contains("App.DeeperHost?.IsRunning == true", svc);
        Assert.Contains("App.Autonomy?.IsActionInProgress == true", svc);
    }

    [Fact]
    public void The_idle_hold_lets_a_prompted_line_through_and_holds_an_unprompted_one()
    {
        // ShouldVoiceNow(false) is always true; the unprompted branch defers to the engine-off rule.
        var idle = Src("ConditioningControlPanel", "Services", "Companion", "CompanionIdleVoice.cs");
        Assert.Contains("!unprompted || ShouldVoice(unprompted", idle);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
