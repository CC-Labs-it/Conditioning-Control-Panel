using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's wallet chip (owner, 2026-10-29): a Sparkles chip in the title bar, signed in only,
/// whose click opens the Prize Counter through the same door as the panel's SparkleWallet. It squishes
/// on press and lets go on release (an IN and an OUT), and the odometer drives its number.
/// </summary>
public class LauncherWalletChipTests
{
    [Fact]
    public void TheChip_IsInTheTitleBar_AndOpensTheCounter()
    {
        var xaml = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml");
        int chip = xaml.IndexOf("x:Name=\"WalletChip\"", StringComparison.Ordinal);
        Assert.True(chip > xaml.IndexOf("x:Name=\"TitleBar\"", StringComparison.Ordinal), "the chip sits in the title bar");
        Assert.True(chip < xaml.IndexOf("x:Name=\"ModPill\"", StringComparison.Ordinal), "next to the account chip");
        var tag = xaml.Substring(chip, xaml.IndexOf('>', chip) - chip);
        Assert.Contains("Click=\"WalletChip_Click\"", tag);
        Assert.Contains("PreviewMouseLeftButtonDown=\"Press_Down\"", tag);
        Assert.Contains("PreviewMouseLeftButtonUp=\"Press_Up\"", tag);
        Assert.Contains("Visibility=\"Collapsed\"", tag);

        var code = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml.cs");
        Assert.Contains("PrizeCounterHostService.Launch(from: \"launcher\")", code);
        var choreo = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.Choreo.cs");
        Assert.Contains("WalletChip.Visibility = show", choreo);
        Assert.Contains("MotionFx.Odometer(SpReadout", choreo);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
