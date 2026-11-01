using System.Linq;
using Avalonia;
using Avalonia.Headless;
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
        var (provider, signedIn, lab) = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasLabProvider = () => true;
            CoreAccount.IsLoggedInProvider = () => true;
            var all = CompanionEffects.Activities();
            Assert.Equal(new[] { "game.breakoutdemo", "game.piecebypiece", "game.dtrh", "game.intake", "page.studio", "page.presets", "page.quests", "page.assets" },
                all.Select(a => a.Id));
            Assert.True(all[3].Allowed);
            CoreAccount.IsLoggedInProvider = () => false;          // the tile would ask to sign in
            Assert.False(all[3].Allowed);
            Assert.True(all[0].Allowed && all[1].Allowed);          // the free demo and the free board need no account
            Assert.False(all[2].Allowed);                            // Down the Rabbit Hole asks to sign in too
            CoreAccount.IsLoggedInProvider = () => true;
            service.Current.AudioOnlySession = true;
            Assert.False(all[3].Allowed);
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

    /// <summary>The AI's lock card follows the user's strict switch and reports accepted queued requests; a
    /// second request while one is open never stacks, so it is reported as not fired.</summary>
    [Fact]
    public void LockCardSurfaceReportsOnlyACardThatShowed()
    {
        var (provider, lab) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider);
        AvaloniaTestDispatcher.Run(() =>
        {
            if (global::Avalonia.Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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

                // WPF App.AiEffects.cs:50-55 and LockCardService.cs:201-210: accepted while queued.
                var bubble = new BubbleCountScheduler(BubbleCountHost.Instance, new BubbleCountQueueTests.Clock(), () => System.Array.Empty<string>());
                var saved = (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider);
                CoreEngine.BubbleCount = bubble; CoreSubliminal.BambiFreezeProvider = () => { };
                try
                {
                    bubble.Trigger(forceTest: true);
                    Assert.True(MantraLockScreenCommand.Surface!("queued mantra", 2));
                    Assert.False(LockCardWindow.IsAnyOpen());
                    Assert.False(MantraLockScreenCommand.Surface!("duplicate", 1));
                    bubble.ForceCleanup(); LockCardWindow.ReplayAfterGame();
                    Assert.Equal("queued mantra", LockCardWindow.Primary!.PhraseShown);
                    Assert.Equal(2, LockCardWindow.Primary.RequiredRepeats);
                }
                finally { bubble.ForceCleanup(); LockCardWindow.ForceCloseAll(); (CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider) = saved; }

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
