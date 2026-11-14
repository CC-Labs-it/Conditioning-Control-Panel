using System;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PopupSwitchesTests (owner 2026-11-07, #4449): the online-media offer, the unasked Discord
/// achievement-share prompt and the tier celebration cards are off. Runs alone: it puts the shipped
/// values back while the popup tests (switches on, TestUserDataProfile) are not running.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PopupSwitchesTests
{
    [Fact]
    public void The_three_popups_ship_off()
    {
        Assert.False(PopupSwitches.ShippedRemoteMediaOffer);
        Assert.False(PopupSwitches.ShippedDiscordSharePrompt);
        Assert.False(PopupSwitches.ShippedTierCelebration);
    }

    [Fact]
    public void Every_door_checks_its_switch()
    {
        var offer = Src("CCP.Avalonia", "Platform", "RemoteMediaOffer.cs");
        Assert.Contains("if (!PopupSwitches.RemoteMediaOffer) return;", offer);
        var login = Src("CCP.Avalonia", "Views", "Windows", "MainShellWindow.Login.cs");
        Assert.Contains("if (!userAsked && !Platform.PopupSwitches.DiscordSharePrompt) return;", login);
        var patreon = Src("CCP.Avalonia", "Views", "Windows", "MainShellWindow.Patreon.cs");
        Assert.Contains("if (!Platform.PopupSwitches.TierCelebration) { TierOnNoticePass(mayShow: false); return; }", patreon);
        Assert.Contains("if (!verdict.Owed || !mayShow || !Platform.PopupSwitches.TierCelebration) return;", patreon);
        int guard = patreon.IndexOf("PopupSwitches.TierCelebration) { TierOnNoticePass", StringComparison.Ordinal);
        int first = patreon.IndexOf("ShowCelebrationIfFirstTime", StringComparison.Ordinal);
        Assert.True(guard > 0 && first > guard);
    }

    [Fact]
    public Task A_switched_off_offer_files_and_opens_nothing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldSource = CoreSettings.Current.MediaSource;
        var oldShow = RemoteMediaOffer.ShowCard;
        var opened = 0;
        StartupLadder.ResetForTests();
        RemoteMediaOffer.ResetForTests();
        RemoteMediaOffer.ShowCard = _ => opened++;
        CoreSettings.Current.MediaSource = "local";
        PopupSwitches.SetAllForTests(false);
        try
        {
            RemoteMediaOffer.Offer("videos");
            RemoteMediaOffer.Park("custom-assets-path-missing");
            RemoteMediaOffer.FlushPending();
            Assert.Equal(0, opened);
            Assert.Empty(StartupLadder.Inbox.Items);
            PopupSwitches.SetAllForTests(true);
            RemoteMediaOffer.Offer("videos");
            Assert.Equal(1, opened);   // the door itself still works behind the switch
        }
        finally
        {
            PopupSwitches.SetAllForTests(true);
            CoreSettings.Current.MediaSource = oldSource;
            RemoteMediaOffer.ShowCard = oldShow;
            RemoteMediaOffer.ResetForTests();
            StartupLadder.ResetForTests();
        }
        return Task.CompletedTask;
    });

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
