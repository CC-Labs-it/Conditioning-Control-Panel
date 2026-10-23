using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Lab.cs: the Possession rung readout (Hook/Update/UnhookPossessionReadout,
/// :880-986) and the first-run rules card (ShowPossessionRulesIfFirstTime, :1100).</summary>
public sealed class LabPossessionReadoutTests
{
    private static void Run(bool introSeen, Action<MainShellWindow, LockdownService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldSettings, oldLockdown) = (CoreSettings.ServiceProvider, LockdownService.Current);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.MotionLevel = MotionLevel.Off;
        s.LockdownDoseKeeperEnabled = false;
        s.LockdownPossessionEnabled = true;
        s.LockdownPossessionIntroSeen = introSeen;
        s.LockdownForceStrictLock = s.LockdownDisablePanicKey = s.LockdownBlockSystemKeys = false;
        if (!s.SeenFeatureIntros.Contains("possession")) s.SeenFeatureIntros.Add("possession");   // no popup window
        using var ld = new LockdownService();
        LockdownService.Current = ld;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell, ld);
        }
        finally
        {
            ld.Deactivate();
            Dispatcher.UIThread.RunJobs();
            shell?.Close();
            StartupLadder.Inbox.Items.Clear();
            (CoreSettings.ServiceProvider, LockdownService.Current) = (oldSettings, oldLockdown);
        }
    });

    private static Color[] Pips(LockdownTabView tab) => tab.FindControl<StackPanel>("PossessionPips")!.Children
        .OfType<Border>().Select(b => ((ISolidColorBrush)b.Background!).Color).ToArray();

    [Fact]
    public void ReadoutStartsAtSettle_FollowsTheDirectorsRung_AndClearsOnExit() => Run(introSeen: true, (shell, ld) =>
    {
        shell.ShowTab("lockdown");
        var tab = shell.Named<LockdownTabView>("LockdownTab")!;
        var text = tab.FindControl<TextBlock>("TxtPossessionRung")!;
        var (ember, dim) = (Color.Parse("#FF8A5C"), Color.Parse("#33FF8A5C"));

        ld.Activate(TimeSpan.FromMinutes(10));
        Dispatcher.UIThread.RunJobs();
        Assert.True(text.IsVisible);
        Assert.Equal(Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_0")), text.Text);
        Assert.Equal(new[] { ember, dim, dim, dim, dim }, Pips(tab));

        // 40 % elapsed -> Melt (PossessionDeck.RungFor); the director raises RungChanged on the tick.
        var start = DateTime.UtcNow;
        ld.UtcNow = () => start + TimeSpan.FromMinutes(4);
        typeof(LockdownService).GetMethod("OnCountdownTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ld, new object?[] { null, EventArgs.Empty });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_2")), text.Text);
        Assert.Equal(new[] { ember, ember, ember, dim, dim }, Pips(tab));

        ld.Deactivate();
        Dispatcher.UIThread.RunJobs();
        Assert.False(text.IsVisible);
        Assert.Equal("", text.Text);
        Assert.All(Pips(tab), c => Assert.Equal(dim, c));
    });

    [Fact]
    public void FirstArmedLockdownPostsTheRules_SpentOnlyWhenOpened() => Run(introSeen: false, (shell, ld) =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromHours(1));   // quiet -> an Inbox row, not an ambush
        ld.Activate(TimeSpan.FromMinutes(10));
        Dispatcher.UIThread.RunJobs();
        var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "intro:possession");
        Assert.False(CoreSettings.Current.LockdownPossessionIntroSeen);
        StartupLadder.Inbox.Open(row);
        Assert.True(CoreSettings.Current.LockdownPossessionIntroSeen);
    });
}
