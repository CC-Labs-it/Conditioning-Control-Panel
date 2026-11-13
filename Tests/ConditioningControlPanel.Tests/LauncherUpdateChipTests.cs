using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's "Update ready" chip (ccp-bugs #1580): players who go straight into a game never
/// reach the panel, where the update dialog waits on the startup ladder. The chip lights from the
/// background check and opens the SAME in-app dialog and updater, never a browser URL; the ladder
/// copy never asks a second time; Lockdown hides it (an install exits the app).
/// </summary>
public class LauncherUpdateChipTests
{
    [Fact]
    public void TheChip_IsInTheTitleBar_HiddenUntilAnUpdate_AndSquishes()
    {
        var xaml = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml");
        int chip = xaml.IndexOf("x:Name=\"UpdateChip\"", StringComparison.Ordinal);
        Assert.True(chip > xaml.IndexOf("x:Name=\"TitleBar\"", StringComparison.Ordinal), "the chip sits in the title bar");
        Assert.True(chip < xaml.IndexOf("x:Name=\"ModPill\"", StringComparison.Ordinal), "with the other title-bar chips");
        var tag = xaml.Substring(chip, xaml.IndexOf('>', chip) - chip);
        Assert.Contains("Style=\"{StaticResource LauncherBarPill}\"", tag);
        Assert.Contains("Click=\"UpdateChip_Click\"", tag);
        Assert.Contains("PreviewMouseLeftButtonDown=\"Press_Down\"", tag);
        Assert.Contains("PreviewMouseLeftButtonUp=\"Press_Up\"", tag);
        Assert.Contains("Visibility=\"Collapsed\"", tag);
    }

    [Fact]
    public void TheChip_OpensTheInAppDialog_NeverAUrl()
    {
        var chip = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.UpdateChip.cs");
        Assert.Contains("App.OfferPendingUpdate(this)", chip);
        Assert.DoesNotContain("Process.Start", chip);
        Assert.DoesNotContain("BrowserLauncher", chip);
        Assert.DoesNotContain("http", chip);

        var app = Read("ConditioningControlPanel", "App.xaml.cs");
        int offer = app.IndexOf("public static void OfferPendingUpdate(", StringComparison.Ordinal);
        Assert.True(offer > 0, "App.OfferPendingUpdate exists");
        var body = app.Substring(offer, app.IndexOf("\n        }", offer, StringComparison.Ordinal) - offer);
        Assert.Contains("ShowUpdateNotification(info, owner)", body);
        Assert.Contains("IsUpdateDialogActive", body);
    }

    [Fact]
    public void TheLadderCopy_SkipsOnceTheChipAsked_AndLockdownHidesTheChip()
    {
        var app = Read("ConditioningControlPanel", "App.xaml.cs");
        int ladder = app.IndexOf("EnqueueModal(\"update-available\"", StringComparison.Ordinal);
        Assert.True(ladder > 0);
        Assert.True(app.IndexOf("if (_updateOffered)", ladder, StringComparison.Ordinal) > ladder);
        int show = app.IndexOf("private void ShowUpdateNotification(", StringComparison.Ordinal);
        Assert.True(app.IndexOf("_updateOffered = true", show, StringComparison.Ordinal) > show);

        var chip = Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.UpdateChip.cs");
        Assert.Contains("App.Lockdown?.IsActive != true", chip);
        // IN and OUT, Gentle respected.
        Assert.Contains("UpdateChipIn()", chip);
        Assert.Contains("UpdateChipOut()", chip);
        Assert.Contains("GentleVisuals.On", chip);
        Assert.Contains("MotionFx.AllowTransitions", chip);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
