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

    internal static void Run(bool punished, Action<MainShellWindow, LauncherWindow, LeashService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.LauncherSoundEnabled) = (true, true, 1, false);
        var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
        var (oldSvc, oldDue, oldPresent) = (LeashHead.Service, MainShellWindow.LeashGateDueProvider, MainShellWindow.PresentLeashGateProvider);
        var oldSelfCut = LeashHead.SelfCut;   // a cut in the body notes it (WPF LeashSurfaces._iCut)
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
            shell.CheckLeashGate(); // Evaluate the timer's shown-panel phase even on a fast machine.
            Assert.Equal(punished, shell.LeashGate!.IsUp);
            Assert.True(LauncherWindow.BackToLauncher(shell));
            shell.StepLauncherFade(1); // with motion on the door first fades the panel (WPF MainWindow.Launcher.cs)
            Dispatcher.UIThread.RunJobs();
            var launcher = LauncherWindow.Instance!;
            launcher.Hide();                             // off screen: no exit beat to wait out
            shell.CheckLeashGate(); // Evaluate the away-panel phase without racing the next 2 s tick.
            body(shell, launcher, svc);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Controls.Leash.LeashAskWindow.CloseCurrent();   // an ask or snap the body left up
            LeashHead.SelfCut = oldSelfCut;
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

    [Theory]
    [InlineData("tutorial")]
    [InlineData("startup")]
    [InlineData("update")]
    public void GateStandsBackForTutorialAndStartupModal(string modal) => Run(true, (shell, launcher, _) =>
    {
        var oldTutorial = CoreTutorial.IsActiveProvider;
        var oldModal = MainShellWindow.IsStartupDialogShowing;
        var offering = typeof(AppUpdater).GetField("_offering", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldOffering = offering.GetValue(null);
        try
        {
            CoreTutorial.IsActiveProvider = () => modal == "tutorial";
            MainShellWindow.IsStartupDialogShowing = modal == "startup";
            offering.SetValue(null, modal == "update");
            PressTile(launcher);
            Assert.False(shell.LeashGate!.IsUp);
            CoreTutorial.IsActiveProvider = () => false;
            MainShellWindow.IsStartupDialogShowing = false;
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

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    /// <summary>Nov 4 review, #4384: a controller joining and leaving while the gate is up must not bring the
    /// browser back over Panic and Cut; the gate hands it back when it goes. A tour still up keeps it hidden
    /// the same way.</summary>
    [Fact]
    public void ControllerLeavingUnderTheGate_KeepsTheBrowserHidden_UntilTheGateGoes() => Run(punished: true, (shell, launcher, _) =>
    {
        var oldTime = MainShellWindow.RemoteOverlayTime;
        var clock = new SteppedClock();
        MainShellWindow.RemoteOverlayTime = clock;
        try
        {
            var browser = shell.LeashBrowser!;
            var overlay = shell.Named<Border>("RemoteControlOverlay")!;
            browser.IsVisible = true;
            PressTile(launcher);
            Assert.True(shell.LeashGate!.IsUp);
            Assert.False(browser.IsVisible);

            overlay.IsVisible = true;          // a controller joins over the gate (ShowRemoteControlOverlay)
            shell.HideRemoteControlOverlay();  // and leaves; the fade runs out
            clock.Now += MainShellWindow.RemoteOverlayFadeOut.Ticks;
            shell.RemoteOverlayTick();
            Assert.False(overlay.IsVisible);
            Assert.False(browser.IsVisible);   // the gate is still up

            var oldSurfaces = PanicSurfaces.All;
            PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
            try { Click(Find(shell, "leash-gate-panic")); }
            finally { PanicSurfaces.All = oldSurfaces; }
            Assert.False(shell.LeashGate.IsUp);
            Assert.True(browser.IsVisible);    // the gate hands it back

            // The help tour's overlay is a cover too: the remote overlay going never shows it over a tour.
            shell.SetTutorialOverlay(true);
            overlay.IsVisible = true;
            shell.HideRemoteControlOverlay();
            clock.Now += MainShellWindow.RemoteOverlayFadeOut.Ticks;
            shell.RemoteOverlayTick();
            Assert.False(browser.IsVisible);
            shell.SetTutorialOverlay(false);
            Assert.True(browser.IsVisible);
        }
        finally { MainShellWindow.RemoteOverlayTime = oldTime; }
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
