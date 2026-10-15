using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopBehaviorBrowserTests
{
    private static void Run(Action<WorkshopBehaviorCell, WebHost> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, false);
        s.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            shell.GetLogicalDescendants().OfType<WorkshopAccordion>().Single().ExpandAndReveal("BEHAVIOR");
            Dispatcher.UIThread.RunJobs();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopBehaviorCell>().Single();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            body(cell, host);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
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
        Assert.Contains(")(false)", sent[2]);
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
        cell.SyncFromSettings();
        Assert.Empty(sent);
        Assert.False(cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!.IsChecked ?? false);
    });
}
