using System.Linq;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
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
        var (provider, signedIn, lab) = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasLabProvider = () => true;
            CoreAccount.IsLoggedInProvider = () => true;
            var all = CompanionEffects.Activities();
            Assert.Equal(new[] { "game.breakoutdemo", "game.piecebypiece", "game.race", "game.dtrh", "game.intake", "page.studio", "page.presets", "page.quests", "page.assets" },
                all.Select(a => a.Id));
            var byId = all.ToDictionary(a => a.Id);
            Assert.True(byId["game.intake"].Allowed);
            Assert.True(byId["game.race"].Allowed);
            CoreEntitlement.HasLabProvider = () => false;
            Assert.False(byId["game.race"].Allowed);              // visible to everyone, offered only to Prime
            Assert.False(byId["game.race"].TryOpen());
            CoreEntitlement.HasLabProvider = () => true;
            Assert.True(byId["game.race"].Allowed);               // access is read live, not cached in the list
            CoreAccount.IsLoggedInProvider = () => false;          // the tile would ask to sign in
            Assert.False(byId["game.intake"].Allowed);
            Assert.True(byId["game.breakoutdemo"].Allowed && byId["game.piecebypiece"].Allowed);
            Assert.False(byId["game.dtrh"].Allowed);
            Assert.False(byId["game.race"].Allowed);
            CoreAccount.IsLoggedInProvider = () => true;
            service.Current.AudioOnlySession = true;
            Assert.False(byId["game.intake"].Allowed);
            Assert.False(byId["game.race"].Allowed);
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (provider, signedIn, lab);
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
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
