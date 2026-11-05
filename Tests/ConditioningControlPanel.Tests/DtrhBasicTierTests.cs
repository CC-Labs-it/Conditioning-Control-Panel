using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Down the Rabbit Hole is a Basic game (owner, 2026-10-31: Racing Thoughts 3D is the Prime game
/// now). Every door keeps the same daily key, so a server drop day still opens it for a free
/// account, and a run only counts as a Daily Gift for an account with no paid grant.
/// </summary>
public class DtrhBasicTierTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "ConditioningControlPanel" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void EveryDoorAsksTheBasicBarWithTheDailyKey()
    {
        Assert.Contains("TierGate.RequiresPremium(\"Down the Rabbit Hole\", \"dtrh\")", Read("App.xaml.cs"));

        var play = Read("MainWindow", "MainWindow.PlayTab.cs");
        Assert.Contains("SetLockband(tab.PlayLockDtrh, TierGate.RequiresPremium(Loc.Get(\"launcher_game_dtrh_title\"), \"dtrh\"));", play);
        Assert.Contains("SetFreeStamp(tab.PlayBadgeDtrh, \"dtrh\", owned: premium);", play);

        var launcher = Read("Services", "Launcher", "LauncherCatalogue.cs");
        Assert.Contains("!PremiumOk(\"launcher_game_dtrh_title\", \"dtrh\")", launcher);
        Assert.Contains("TierGate.RequiresPremium(Loc.Get(\"launcher_game_dtrh_title\"), \"dtrh\")", launcher);
        Assert.Contains("TierGate.DemandPremium(Loc.Get(\"launcher_game_dtrh_title\"), \"dtrh\")", launcher);

        var emi = Read("Services", "EmiDesk", "EmiTargets.cs");
        Assert.Contains("!PremiumOk(\"emi_desk_target_dtrh\", \"dtrh\")", emi);
        Assert.Contains("case \"dtrh\": PremiumPrompt(key, \"dtrh\");", emi);
    }

    [Fact]
    public void ThePlayCardWearsTheBasicBand()
    {
        var page = Read("Views", "Tabs", "PlayTabView.xaml");
        Assert.Contains("x:Name=\"PlayLockDtrh\" x:FieldModifier=\"internal\" Style=\"{StaticResource PlayLockBand}\"", page);
        var band = page.Substring(page.IndexOf("x:Name=\"PlayLockDtrh\"", StringComparison.Ordinal), 400);
        Assert.Contains("hm3_rail_lock_t1", band);
        Assert.DoesNotContain("hm3_rail_lock_t2", band);
    }

    [Fact]
    public void TheMysteryBoxAndTheShelfSayTierOne()
    {
        Assert.Contains("\"dtrh\" or \"takeover\" or \"awareness\" or \"haptics\" or \"voice\" or \"fyp\" or \"remote\" => 1,",
            Read("MainWindow", "MainWindow.Presets.cs"));

        var card = ExclusiveFeature.All.Single(f => f.Key == "dtrh");
        Assert.Equal(1, card.Tier);
        Assert.Equal("dtrh", card.DailyFreeKey);
        Assert.Null(card.Gate);   // the default premium gate
    }

    [Fact]
    public void ThePaidPlayerNeverGetsTheDailyGiftEndCard()
    {
        var host = Read("Services", "Chaos", "DtrhHostService.cs");
        Assert.Contains("RunCameFromDailyGift(_testMode, App.Patreon?.HasPremiumAccess == true,", host);
    }

    [Fact]
    public void TheVaultListsItUnderBasic()
    {
        var vault = Read("Dialogs", "VaultGateDialog.cs");
        Assert.Contains("\"vaultgate_basic_10\"", vault);
        Assert.DoesNotContain("vaultgate_prime_2", vault);
    }
}
