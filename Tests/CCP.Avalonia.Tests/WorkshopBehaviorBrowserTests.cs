using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopBehaviorBrowserTests
{
    private static void Run(Action<WorkshopBehaviorCell, WebHost> body, bool avatar = false) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, avatar);
        s.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            // Companion v2 collapses the room; the Behaviour cell's deep link opens Companion > AI, which adopts it.
            shell.RevealCompanionWorkshopCell(CompanionRoomAnchors.WorkshopBehaviorCell);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("companionai", shell.CurrentTab);
            var cell = shell.Named<AiPage>("CompanionAiTab")!.GetLogicalDescendants().OfType<WorkshopBehaviorCell>().Single();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            body(cell, host);
        }
        finally
        {
            if (avatar) shell.RequestExit(); else shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
        }
    });

    [Fact]
    public void PauseSwitchMutesAndPausesTheHomePageThenResumesWithoutChangingTheSavedMute() => Run((cell, host) =>
    {
        CoreSettings.Current.BrowserVideoMuted = true;
        var sent = new List<string>();
        host.ScriptInvoker = script =>
        {
            sent.Add(script);
            return Task.FromResult<string?>(script.Contains("return 'paused'") ? "\"paused\"" :
                script.Contains("return 'playing'") ? "playing" : script.Contains(")(true)") ? "muted" : "unmuted");
        };
        var toggle = cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, sent.Count);
        Assert.Contains("__ccpMuted", sent[0]);
        Assert.Contains(")(true)", sent[0]);
        Assert.Contains("el.pause()", sent[1]);
        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, sent.Count);
        Assert.Contains(")(true)", sent[2]); // resume keeps the saved mute: header icon and sound agree
        Assert.Contains("el.play()", sent[3]);
        Assert.True(CoreSettings.Current.BrowserVideoMuted); // Pause is live state, not this persisted preference.
    });

    [Fact]
    public void ANavigationWhilePausedKeepsTheNewPageMutedEvenWhenTheSavedSettingIsUnmuted() => Run((cell, host) =>
    {
        CoreSettings.Current.BrowserVideoMuted = false;
        var sent = new List<string>();
        host.ScriptInvoker = script => { sent.Add(script); return Task.FromResult<string?>("muted"); };
        var toggle = cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        sent.Clear();
        var tab = host.FindLogicalAncestorOfType<SettingsTabView>()!;
        tab.ApplyBrowserMute(); // what every NavigationCompleted runs
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(")(true)", Assert.Single(sent));
        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        sent.Clear();
        tab.ApplyBrowserMute();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(")(false)", Assert.Single(sent)); // resumed: the saved preference rules again
        Assert.False(CoreSettings.Current.BrowserVideoMuted);
    });

    [Fact]
    public void RevealDoesNotReplayThePauseCommandOrPersistIt() => Run((cell, host) =>
    {
        var sent = new List<string>();
        host.ScriptInvoker = script => { sent.Add(script); return Task.FromResult<string?>("paused"); };
        var toggle = cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        sent.Clear();
        cell.SyncFromSettings(); // a reveal re-syncs; it must neither reset the live switch nor resend a command
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
        Assert.True(toggle.IsChecked);
    });
    /// <summary>WPF MenuItemPauseBrowser_Click -> SyncQuickControlsUI(pauseBrowser) and
    /// ChkPauseBrowser_Changed -> AvatarTubeWindow.SetBrowserPaused: the tube item and the Workshop
    /// switch mirror each other, and the mirror never re-sends the pause.</summary>
    [Fact]
    public void TubePauseItemAndWorkshopSwitchMirrorEachOther() => Run((cell, host) =>
    {
        var sent = new List<string>();
        host.ScriptInvoker = script => { sent.Add(script); return Task.FromResult<string?>("paused"); };
        var tube = ((MainShellWindow)TopLevel.GetTopLevel(cell)!).Tube!;
        var item = tube.FindControl<MenuItem>("MenuItemPauseBrowser")!;
        var toggle = cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsChecked);
        Assert.Equal(2, sent.Count); // mute + pause once; the mirrored switch did not replay it
        Assert.Contains("el.pause()", sent[1]);
        Assert.Equal("menu_resume_browser", (item.Header as IconLabel)?.Key);

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("el.play()", sent[^1]);
        Assert.Equal("menu_pause_browser", (item.Header as IconLabel)?.Key);
    }, avatar: true);

    [Fact]
    public void WhisperMuteRefreshesTheOpenTubeMenuWithoutDisablingWhispers() => Run((cell, _) =>
    {
        var tube = ((MainShellWindow)TopLevel.GetTopLevel(cell)!).Tube!;
        CoreSettings.Current.SubAudioEnabled = true;
        CoreSettings.Current.SubAudioMuted = false;
        cell.SyncFromSettings();
        tube.UpdateQuickMenuState();
        cell.FindControl<CheckBox>("ChkMuteWhispersCompanion")!.IsChecked = true;
        Assert.True(CoreSettings.Current.SubAudioEnabled);
        Assert.Equal("menu_mute_whispers_on",
            (tube.FindControl<MenuItem>("MenuItemMuteWhispers")!.Header as IconLabel)?.Key);
    }, avatar: true);

    [Fact]
    public void MidnightArtUsesBothPanesAndYieldsToModGlass() => Run((cell, _) =>
    {
        var path = System.IO.Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        System.IO.Directory.CreateDirectory(CorePaths.UserData);
        System.IO.File.WriteAllText(path, "{\"wallet\":{\"inv\":{\"tube_midnight\":{\"n\":1}}}}");
        CoreSettings.Current.TubeMidnightGlass = true;
        var tube = ((MainShellWindow)TopLevel.GetTopLevel(cell)!).Tube!;
        var previous = CoreModArt.OverridePathProvider;
        var requested = new List<string>();
        try
        {
            CoreModArt.OverridePathProvider = p => { requested.Add(p); return null; };
            tube.SetTubeStyle(false);
            tube.SetTubeStyle(true);
            Assert.Contains("tube_midnight.png", requested);
            Assert.Contains("tube2_midnight.png", requested);
            CoreModArt.OverridePathProvider = p => p == "tube2.png" ? "/mod/tube2.png" : null;
            Assert.False(global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.MidnightGlassWanted());
        }
        finally { CoreModArt.OverridePathProvider = previous; }
    }, avatar: true);

    /// <summary>WPF SyncTubeMidnightRow + MidnightGlassWanted: the row and the tube's glass follow the
    /// persisted wallet (ArcademyHostService.WalletOwnsSku), greyed when the prize was not sold.</summary>
    [Fact]
    public void MidnightGlassRowAndTubeFollowTheSavedWallet() => Run((cell, _) =>
    {
        var path = System.IO.Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        var backup = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        var row = cell.FindControl<CheckBox>("ChkTubeMidnightGlass")!;
        try
        {
            CoreSettings.Current.TubeMidnightGlass = true;
            System.IO.File.Delete(path);
            cell.SyncFromSettings();
            Assert.False(row.IsEnabled);
            Assert.False(row.IsChecked);
            Assert.False(global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.MidnightGlassWanted());

            System.IO.Directory.CreateDirectory(CorePaths.UserData);
            System.IO.File.WriteAllText(path, "{\"wallet\":{\"inv\":{\"tube_midnight\":{\"n\":1}}}}");
            cell.SyncFromSettings();
            Assert.True(row.IsEnabled);
            Assert.True(row.IsChecked);
            Assert.True(global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.MidnightGlassWanted());
            row.IsChecked = false;
            Assert.False(CoreSettings.Current.TubeMidnightGlass);
            Assert.False(global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.MidnightGlassWanted());
        }
        finally
        {
            if (backup != null) System.IO.File.WriteAllText(path, backup); else System.IO.File.Delete(path);
        }
    });
}
