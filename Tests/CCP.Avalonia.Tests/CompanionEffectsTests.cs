using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>CompanionBrain slice 4 on this head: the brain's commands reach Core's gate, a surface
/// this head lacks is refused (never faked), and the capability list holds only what opens here.</summary>
public sealed class CompanionEffectsTests
{
    [Fact]
    public void SeedWiresTheGateAndLeavesAMissingSurfaceRefused()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            CompanionEffects.Seed();
            Assert.NotNull(CompanionBrain.CommandExecutor);
            Assert.NotNull(CompanionBrain.ActivitiesProvider);
            Assert.Null(SpiralCommand.Surface);                     // no spiral overlay port
            // No main window / click-through here: the overlay surface refuses instead of claiming a flash.
            Assert.False(FlashImageCommand.Surface!(3, 1000, 100));
        });
    }

    [Fact]
    public void ActivitiesOfferOnlyWhatOpensHere()
    {
        var (provider, signedIn, lab, premium) = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider);
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasLabProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => true;   // DtRH is Basic (owner 2026-10-31); the race stays Prime
            CoreAccount.IsLoggedInProvider = () => true;
            var all = CompanionEffects.Activities();
            // WPF CompanionActivities.cs:15 offers every LauncherCatalogue game, Classic included (:249).
            Assert.Equal(new[] { "game.breakoutdemo", "game.breakout", "game.piecebypiece", "game.race", "game.raceclassic", "game.dtrh", "game.intake", "page.studio", "page.presets", "page.quests", "page.assets" },
                all.Select(a => a.Id));
            var breakout = all.Single(a => a.Id == "game.breakout");
            var race = all.Single(a => a.Id == "game.race");
            var intake = all.Single(a => a.Id == "game.intake");
            var dtrh = all.Single(a => a.Id == "game.dtrh");
            Assert.True(breakout.Allowed && race.Allowed && intake.Allowed && dtrh.Allowed);
            CoreEntitlement.HasLabProvider = () => false;
            Assert.False(breakout.Allowed);
            Assert.False(race.Allowed);                             // the Companion respects the same Prime door
            Assert.True(all.Single(a => a.Id == "game.raceclassic").Allowed);   // Classic is free (LauncherCatalogue.cs:249)
            CoreEntitlement.HasLabProvider = () => true;
            Assert.True(race.Allowed);                              // the cached activity rechecks entitlement
            CoreAccount.IsLoggedInProvider = () => false;          // the tile would ask to sign in
            Assert.False(intake.Allowed);
            Assert.False(race.Allowed);
            Assert.True(all.Single(a => a.Id == "game.breakoutdemo").Allowed && all.Single(a => a.Id == "game.piecebypiece").Allowed);          // the free demo and the free board need no account
            Assert.False(breakout.Allowed);
            Assert.False(dtrh.Allowed);
            CoreAccount.IsLoggedInProvider = () => true;
            service.Current.AudioOnlySession = true;
            Assert.False(intake.Allowed);
            Assert.False(race.Allowed);
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider) = (provider, signedIn, lab, premium);
        }
    }

    /// <summary>Audit #1982: a refused AI pink filter (nothing could show) leaves the user's saved
    /// Pink filter switch and opacity exactly as they were.</summary>
    [Fact]
    public void RefusedPinkFilterLeavesTheSavedSettingsAlone()
    {
        var (provider, lab) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider);
        AvaloniaTestDispatcher.Run(() =>
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.CompanionPrompt.AllowAiToControlEffects = true;
            service.Current.PinkFilterEnabled = false;
            service.Current.PinkFilterOpacity = 10;
            CoreAccount.HasLabAccessProvider = () => true;
            try
            {
                CompanionEffects.Seed();
                Assert.False(PinkCommand.Surface!(true, 25));   // no host here: the tint cannot show
                Assert.False(service.Current.PinkFilterEnabled);
                Assert.Equal(10, service.Current.PinkFilterOpacity);
                // Main bafe52cfc: an AI "off" carries no opacity and never zeroes the user's.
                PinkCommand.Surface!(false, null);
                Assert.Equal(10, service.Current.PinkFilterOpacity);
            }
            finally
            {
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider) = (provider, lab);
            }
        });
    }

    [Fact]
    public void PinkCommandShowsWithEngineOffAndPanicClearsIt() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (provider, lab, engine) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider);
        var (skip, query, start, remoteRefresh) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, CompanionEffects.StartEffect, RemoteCommands.RefreshOverlays);
        var service = new SettingsService();
        var host = new Window();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.HasLabAccessProvider = () => true;
            CoreSession.IsEngineRunningProvider = () => false;
            service.Current.CompanionPrompt.AllowAiToControlEffects = true;
            service.Current.GentleVisuals = false;
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            CompanionEffects.StartEffect = action => action();
            var screen = (global::Avalonia.Platform.Screen)System.Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
                new object[] { new global::Avalonia.Platform.PlatformHandle(new System.IntPtr(1), "headless-test") })!;
            typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, 1.0);
            typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, new PixelRect(0, 0, 800, 600));
            typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, new PixelRect(0, 0, 800, 600));
            typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, true);
            ScreenList.QueryOverride = _ => new[] { screen };
            host.Show();
            RemoteCommands.RefreshOverlays = () => PinkFilterOverlay.Refresh(host);
            RemoteCommands.StopOverlays(); // A new AI command can start after an earlier engine stop.
            Assert.True(CompanionEffects.ApplyPink(host, true, 25));
            Assert.True(PinkFilterOverlay.IsShowing);
            Assert.Equal(25, service.Current.PinkFilterOpacity);
            PanicSurfaces.All.Single(s => s.Id == "voice-overlays").Stop(null);
            Assert.False(PinkFilterOverlay.IsShowing);
            PinkFilterOverlay.Refresh(host);
            Assert.False(PinkFilterOverlay.IsShowing);

            System.Action? pending = null;
            CompanionEffects.StartEffect = action => pending = action;
            Assert.True(CompanionEffects.ApplyPink(host, true, 30));
            PanicSurfaces.All.Single(s => s.Id == "voice-overlays").Stop(null);
            pending!();
            Assert.False(PinkFilterOverlay.IsShowing);
            CompanionEffects.StartEffect = action => action();

            Assert.True(CompanionEffects.ApplyPink(host, true, 25));
            RemoteCommands.StopOverlays();
            Assert.False(PinkFilterOverlay.IsShowing);
            RemoteCommands.ReleaseOverlayStop();
            PinkFilterOverlay.Refresh(host);
            Assert.False(PinkFilterOverlay.IsShowing);
        }
        finally
        {
            RemoteCommands.RefreshOverlays = remoteRefresh;
            RemoteCommands.ReleaseOverlayStop();
            PinkFilterOverlay.Stop(); host.Close();
            (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, CompanionEffects.StartEffect) = (skip, query, start);
            (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider) = (provider, lab, engine);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async System.Threading.Tasks.Task PinkCommandStartsEnabledSpiralWithEngineOff(bool pinkOn)
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-ai-pink-spiral-");
        var gif = System.IO.Path.Combine(dir.FullName, "spiral.gif");
        System.IO.File.WriteAllBytes(gif, System.Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                if (SpiralOverlay.Decoding is { } prior) await prior;
                var (provider, lab, engine) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider);
                var (pinkChecks, spiralChecks, query, start, remoteRefresh) = (PinkFilterOverlay.SkipPlatformChecksForTests,
                    SpiralOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, CompanionEffects.StartEffect, RemoteCommands.RefreshOverlays);
                var service = new SettingsService();
                var host = new Window();
                try
                {
                    PinkFilterOverlay.Stop(); SpiralOverlay.CloseAll();
                    CoreSettings.ServiceProvider = () => service;
                    CoreAccount.HasLabAccessProvider = () => true;
                    CoreSession.IsEngineRunningProvider = () => false;
                    service.Current.CompanionPrompt.AllowAiToControlEffects = true;
                    service.Current.GentleVisuals = false;
                    service.Current.SpiralEnabled = true;
                    service.Current.SpiralRandomize = false;
                    service.Current.SpiralPath = gif;
                    PinkFilterOverlay.SkipPlatformChecksForTests = true;
                    SpiralOverlay.SkipPlatformChecksForTests = true;
                    CompanionEffects.StartEffect = action => action();
                    var screen = (global::Avalonia.Platform.Screen)System.Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
                        new object[] { new global::Avalonia.Platform.PlatformHandle(new System.IntPtr(1), "headless-test") })!;
                    typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, 1.0);
                    typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, new PixelRect(0, 0, 800, 600));
                    typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, new PixelRect(0, 0, 800, 600));
                    typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, true);
                    ScreenList.QueryOverride = _ => new[] { screen };
                    host.Show();
                    Assert.True(CompanionEffects.ApplyPink(host, pinkOn, pinkOn ? 25 : null));
                    if (SpiralOverlay.Decoding is { } pending) await pending;
                    Assert.True(SpiralOverlay.IsShowing);
                    Assert.Equal(pinkOn, PinkFilterOverlay.IsShowing);
                    // A remote stop must close the actual window, even when gentle fade is enabled.
                    var window = SpiralOverlay.Shown[0];
                    service.Current.GentleVisuals = true;
                    RemoteCommands.RefreshOverlays = () => { PinkFilterOverlay.Refresh(host); SpiralOverlay.Refresh(host); };
                    RemoteCommands.StopOverlays();
                    Assert.False(window.IsVisible);
                    Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsReconciling || PinkFilterOverlay.IsShowing);

                    service.Current.GentleVisuals = false;
                    Assert.True(CompanionEffects.ApplyPink(host, pinkOn, pinkOn ? 25 : null));
                    if (SpiralOverlay.Decoding is { } restart) await restart;
                    Assert.True(SpiralOverlay.IsShowing);
                    var second = System.IO.Path.Combine(dir.FullName, "second.gif");
                    System.IO.File.Copy(gif, second, true);
                    service.Current.SpiralPath = second;
                    SpiralOverlay.Refresh(host);
                    var pendingStop = SpiralOverlay.Decoding;
                    Assert.NotNull(pendingStop);
                    PanicSurfaces.All.Single(s => s.Id == "voice-overlays").Stop(null);
                    await pendingStop!;
                    Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsReconciling || PinkFilterOverlay.IsShowing);
                    SpiralOverlay.Refresh(host);
                    Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsReconciling);
                }
                finally
                {
                    SpiralOverlay.CloseAll(); PinkFilterOverlay.Stop(); host.Close();
                    (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests,
                        ScreenList.QueryOverride, CompanionEffects.StartEffect, RemoteCommands.RefreshOverlays)
                        = (pinkChecks, spiralChecks, query, start, remoteRefresh);
                    (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider) = (provider, lab, engine);
                }
            });
        }
        finally { dir.Delete(true); }
    }

    /// <summary>The AI's lock card follows the user's strict switch and reports "fired" only when a card really came up: a
    /// second request while one is open never stacks, so it is reported as not fired.</summary>
    [Fact]
    public void LockCardSurfaceReportsOnlyACardThatShowed()
    {
        var (provider, lab) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider);
        AvaloniaTestDispatcher.Run(() =>
        {
            if (global::Avalonia.Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.CompanionPrompt.AllowAiToControlEffects = true;
            CoreAccount.HasLabAccessProvider = () => true;
            try
            {
                CompanionEffects.Seed();
                Assert.True(MantraLockScreenCommand.Surface!("good girls obey", 1));
                Assert.True(LockCardWindow.IsAnyOpen());
                Assert.False(MantraLockScreenCommand.Surface!("again", 1));
                LockCardWindow.ForceCloseAll();

                // Held behind the portal panic bind, then a panic: the start must be dropped when released.
                System.Action? held = null;
                CompanionEffects.StartEffect = start => held = start;
                Assert.True(MantraLockScreenCommand.Surface!("held", 1));   // requested, not yet shown
                Assert.False(LockCardWindow.IsAnyOpen());
                MainShellWindow.CancelPendingAi();
                held!();
                Assert.False(LockCardWindow.IsAnyOpen());
                CompanionEffects.StartEffect = MainShellWindow.StartEffect;

                // Owner 2026-10-22 (main 61eeb4d0d): an AI card follows the user's own Strict Lock Card
                // switch, never forced strict.
                service.Current.LockCardStrict = false;
                Assert.True(MantraLockScreenCommand.Surface!("not strict", 1));
                Assert.False(LockCardWindow.Primary!.IsStrict);
                LockCardWindow.ForceCloseAll();
                service.Current.LockCardStrict = true;
                Assert.True(MantraLockScreenCommand.Surface!("strict", 1));
                Assert.True(LockCardWindow.Primary!.IsStrict);
                LockCardWindow.ForceCloseAll();

                service.Current.CompanionPrompt.AllowAiToControlEffects = false;
                Assert.False(MantraLockScreenCommand.Surface!("no consent", 1));
                Assert.False(LockCardWindow.IsAnyOpen());
            }
            finally
            {
                CompanionEffects.StartEffect = MainShellWindow.StartEffect;
                LockCardWindow.ForceCloseAll();
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider) = (provider, lab);
            }
        });
    }
}
