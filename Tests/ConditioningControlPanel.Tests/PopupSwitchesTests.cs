using System.IO;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner 2026-11-07 (7.2.3): three popups are off - the "Media without the download" online-media
/// offer, the unasked Discord achievement-share prompt, and the tier celebration cards. Each door
/// checks its switch before it shows anything.
/// </summary>
public class PopupSwitchesTests
{
    [Fact]
    public void The_three_popups_are_off()
    {
        Assert.False(PopupSwitches.RemoteMediaOffer);
        Assert.False(PopupSwitches.DiscordSharePrompt);
        Assert.False(PopupSwitches.TierCelebration);
    }

    [Fact]
    public void Every_door_checks_its_switch()
    {
        var app = Src("ConditioningControlPanel", "App.xaml.cs");
        Assert.Contains("if (!Services.Startup.PopupSwitches.RemoteMediaOffer) return;", app);

        var patreon = Src("ConditioningControlPanel", "MainWindow", "MainWindow.Patreon.cs");
        Assert.Contains("if (!userAsked && !Services.Startup.PopupSwitches.DiscordSharePrompt) return;", patreon);
        Assert.Contains("if (!Services.Startup.PopupSwitches.TierCelebration) { TierOnNoticePass(mayShow: false); return; }", patreon);
        Assert.Contains("if (!verdict.Owed || !mayShow || !Services.Startup.PopupSwitches.TierCelebration) return;", patreon);
    }

    [Fact]
    public void The_celebration_card_is_only_shown_from_the_guarded_method()
    {
        // ShowCelebrationIfFirstTime has exactly the two call sites inside MaybeShowPremiumCelebration,
        // both after the switch; a new caller must check the switch too.
        var patreon = Src("ConditioningControlPanel", "MainWindow", "MainWindow.Patreon.cs");
        int guard = patreon.IndexOf("PopupSwitches.TierCelebration) { TierOnNoticePass", System.StringComparison.Ordinal);
        int first = patreon.IndexOf("ShowCelebrationIfFirstTime", System.StringComparison.Ordinal);
        Assert.True(guard > 0 && first > guard);
        foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (f.EndsWith("MainWindow.Patreon.cs") || f.EndsWith("FeatureIntroPopup.xaml.cs")) continue;
            Assert.DoesNotContain("ShowCelebrationIfFirstTime(", File.ReadAllText(f));
        }
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "AvatarTube")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
