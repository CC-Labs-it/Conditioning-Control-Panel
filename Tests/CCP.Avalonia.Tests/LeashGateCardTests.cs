using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>leash-gate-card (WPF LeashGateCard.cs, MainWindow.Leash.cs, LauncherHost.cs:360-366): a launcher
/// tile under a pending punishment opens the panel with THE GATE on it; Panic and Cut leash always work;
/// with nothing pending the tile opens its game. Driven from the launcher tile, on the real Core LeashService.</summary>
public sealed class LeashGateCardTests
{
    private sealed class Targets : LeashCutSafety.ITargets
    {
        public int Released;
        public bool LockdownActive => false;
        public void EndLockdown() { }
        public void DiscardLockdownRecovery() { }
        public bool RemoteActive => false;
        public void EndRemote() { }
        public bool StrictVideoRunning => false;
        public void StopVideo() { }
        public void ReleaseSafetySettings() => Released++;
        public bool DropUnpushedLeashBookings() => false;
    }

    internal static void EnsureHeadless()
    {
        if (Application.Current is not null) return;
        var fonts = AppFonts.Options();
        var mappings = new Dictionary<string, global::Avalonia.Media.FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    internal static void Run(bool punished, Action<MainShellWindow, LauncherWindow, LeashService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureHeadless();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.LauncherSoundEnabled) = (true, true, 1, false);
        var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
        var (oldSvc, oldDue, oldPresent) = (LeashHead.Service, MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider);
        (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (() => true, () => true);
        var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        if (punished) svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        // What LeashHead.Start publishes (LeashHeadTests proves Start sets them).
        LeashHead.Service = svc;
        MainShellWindow.LeashGateDueProvider = () => LeashHead.Service?.GateDue != null;
        MainShellWindow.PresentLeashGateProvider = sh => sh.PresentLeashGate();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(LauncherWindow.BackToLauncher(shell));
            shell.StepLauncherFade(1); // with motion on the door first fades the panel (WPF MainWindow.Launcher.cs)
            Dispatcher.UIThread.RunJobs();
            var launcher = LauncherWindow.Instance!;
            launcher.Hide();                             // off screen: no exit beat to wait out
            body(shell, launcher, svc);
        }
        finally
        {
            LauncherWindow.Instance?.Close();
            (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (oldIn, oldLab);
            (LeashHead.Service, MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider) = (oldSvc, oldDue, oldPresent);
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
    });

    internal static void PressTile(LauncherWindow launcher)
    {
        launcher.Play(LauncherCards.Find("intake")!);
        Dispatcher.UIThread.RunJobs();
    }

    internal static Button Find(MainShellWindow shell, string tag) =>
        shell.LeashGate!.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Tag == tag);

    internal static void Click(Button b)
    {
        Assert.True(b.IsEffectivelyEnabled);
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void GateWaitsForTutorialAndStartupDialogs() => Run(true, (shell, launcher, _) =>
    {
        var oldTutorial = CoreTutorial.IsActiveProvider;
        var oldModal = MainShellWindow.IsStartupDialogShowing;
        var offering = typeof(AppUpdater).GetField("_offering", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldOffering = offering.GetValue(null);
        try
        {
            CoreTutorial.IsActiveProvider = () => true;
            PressTile(launcher);
            Assert.False(shell.LeashGate!.IsUp);
            CoreTutorial.IsActiveProvider = () => false;
            MainShellWindow.IsStartupDialogShowing = true;
            shell.CheckLeashGate();
            Assert.False(shell.LeashGate.IsUp);
            MainShellWindow.IsStartupDialogShowing = false;
            offering.SetValue(null, true);
            shell.CheckLeashGate();
            Assert.False(shell.LeashGate.IsUp);
            offering.SetValue(null, false);
            shell.CheckLeashGate();
            Assert.True(shell.LeashGate.IsUp);
        }
        finally
        {
            CoreTutorial.IsActiveProvider = oldTutorial;
            MainShellWindow.IsStartupDialogShowing = oldModal;
            offering.SetValue(null, oldOffering);
        }
    });

    [Fact]
    public void PendingPunishment_TileShowsTheGateCard_WithPanicAndCut() => Run(punished: true, (shell, launcher, _) =>
    {
        Assert.False(shell.LeashGate!.IsUp);
        PressTile(launcher);
        Assert.True(shell.IsVisible);
        Assert.True(shell.LeashGate.IsUp);
        Assert.Equal("p1", shell.LeashGate.Pid);
        Assert.NotEqual("gradedintake", shell.CurrentTab);
        Assert.Equal(3, shell.LeashGate.ProgressShown.Total);   // Lines x3: three pips
        Assert.True(Find(shell, "leash-gate-panic").IsEffectivelyEnabled);
        Assert.True(Find(shell, "leash-gate-cut").IsEffectivelyEnabled);
        Assert.True(Find(shell, "leash-gate-pardon").IsEffectivelyEnabled);   // one pardon held
    });

    [Fact]
    public void Panic_ClosesTheGate_RunsThePanicStop_AndSnoozesUntilTheNextTile() => Run(punished: true, (shell, launcher, _) =>
    {
        var oldSurfaces = PanicSurfaces.All;
        var stops = new List<string>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("probe")) };
        var s = CoreSettings.Current;
        var oldKey = s.PanicKeyEnabled;
        try
        {
            s.PanicKeyEnabled = false;                  // the gate's Panic never asks the global key's switch
            PressTile(launcher);
            Click(Find(shell, "leash-gate-panic"));
            Assert.False(shell.LeashGate!.IsUp);
            Assert.Equal(new[] { "probe" }, stops);
            shell.CheckLeashGate();                     // the 2 s check: still standing back
            Assert.False(shell.LeashGate.IsUp);
            PressTile(launcher);                        // a tile press ends the snooze (WPF)
            Assert.True(shell.LeashGate.IsUp);
        }
        finally { PanicSurfaces.All = oldSurfaces; s.PanicKeyEnabled = oldKey; }
    });

    [Fact]
    public void CutLeash_EndsIt_ThroughLeashCutSafety() => Run(punished: true, (shell, launcher, svc) =>
    {
        var (oldTargets, oldUi) = (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi);
        var targets = new Targets();
        (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi) = (targets, a => a());
        try
        {
            PressTile(launcher);
            Click(Find(shell, "leash-gate-cut"));
            Assert.False(shell.LeashGate!.IsUp);
            Assert.Equal(1, targets.Released);
            Assert.Null(svc.Snapshot.Me);
            Assert.False(MainShellWindow.LeashBlocksGames);
            shell.CheckLeashGate();
            Assert.False(shell.LeashGate.IsUp);
        }
        finally { (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi) = (oldTargets, oldUi); }
    });

    /// <summary>WPF MainWindow.Leash.cs:184-201: the native browser never sits over Panic and Cut, and comes back
    /// when the gate goes. A playable task carries no "Not now" (WPF: only an unplayable video does).</summary>
    [Fact]
    public void TheBrowserHidesUnderTheGate_AndComesBackWhenItGoes() => Run(punished: true, (shell, launcher, _) =>
    {
        var browser = shell.LeashBrowser!;
        browser.IsVisible = true;
        PressTile(launcher);
        Assert.True(shell.LeashGate!.IsUp);
        Assert.False(browser.IsVisible);
        Assert.DoesNotContain(shell.LeashGate.GetLogicalDescendants().OfType<Button>(), b => (string?)b.Tag == "leash-gate-later");
        var oldSurfaces = PanicSurfaces.All;
        PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
        try { Click(Find(shell, "leash-gate-panic")); }
        finally { PanicSurfaces.All = oldSurfaces; }
        Assert.False(shell.LeashGate.IsUp);
        Assert.True(browser.IsVisible);
    });

    [Fact]
    public void NoPunishment_TileOpensItsGame() => Run(punished: false, (shell, launcher, _) =>
    {
        Assert.False(MainShellWindow.LeashBlocksGames);
        PressTile(launcher);
        Assert.False(shell.LeashGate!.IsUp);
        Assert.Equal("gradedintake", shell.CurrentTab);
    });
}
