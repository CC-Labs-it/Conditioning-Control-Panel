using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Toy haptics are free for every account (owner, 2026-11-11; they were Basic). Every haptics
/// door asks <see cref="HapticsAccess"/>, so none of them may re-ask the premium bar on its own.
/// </summary>
public class HapticsFreeTierTests
{
    // SourceRoots: HapticMixer and DailyFreeService live in CCP.Core on the port.
    private static string Read(params string[] parts) => SourceRoots.ReadProductFile(parts);

    [Fact]
    public void ToysAreOpenForEveryone() => Assert.True(HapticsAccess.IsOpen);

    [Fact]
    public void TheExclusivesCardIsAFreeCardWithNoDailyKey()
    {
        var card = ExclusiveFeature.All.Single(f => f.Key == "haptics");
        Assert.Equal(0, card.Tier);
        Assert.Null(card.DailyFreeKey);
        Assert.Equal(ExclusiveGateState.Unlocked, card.GateState());
    }

    [Theory]
    [InlineData("Services", "Haptics", "Core", "HapticMixer.cs")]
    [InlineData("MainWindow", "MainWindow.Haptics.cs")]
    [InlineData("Windows", "HapticsSetupWindow.xaml.cs")]
    public void NoHapticsDoorAsksThePremiumBar(params string[] path)
    {
        var src = Read(path);
        Assert.Contains("HapticsAccess.IsOpen", src);
        Assert.DoesNotContain("IsFreeToday(\"haptics\")", src);
    }

    [Fact]
    public void TheVeilAndTheLapseSweepFollowTheSameAnswer()
    {
        var src = Read("MainWindow", "MainWindow.Patreon.cs");
        Assert.DoesNotContain("IsFreeToday(\"haptics\")", src);
        Assert.DoesNotContain("RefreshPremiumGate(HapticsTab?.HapticsGate", src);
        Assert.Contains("var hasHapticsAccess = Services.Haptics.HapticsAccess.IsOpen;", src);
    }

    [Fact]
    public void TheServerCanNoLongerNameHapticsAndBasicNoLongerSellsIt()
    {
        Assert.DoesNotContain("\"haptics\"", Read("Services", "DailyFreeService.cs").Split("OverridableKeys =")[1].Split(';')[0]);
        var vault = Read("Dialogs", "VaultGateDialog.cs");
        var basic = vault.Split("BasicPerks =")[1].Split("};")[0];
        Assert.DoesNotContain("\"vaultgate_basic_4\"", basic);
    }
}
