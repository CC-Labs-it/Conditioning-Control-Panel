using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-patreon / shell-subscribestar: WPF MaybeShowPremiumCelebration, reached from a provider's
/// TierChanged (OnPatreonTierChanged / OnSubscribeStarTierChanged) and from the launch re-check.</summary>
public sealed class PremiumCelebrationTests
{
    private static void Run(bool premiumAtLaunch, Action<MainShellWindow, List<Window>, Action<bool, bool>> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var oldSettings = CoreSettings.ServiceProvider;
            var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
            var settingsBefore = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;                      // no first-run wizard holding the presenter
            s.HasAcceptedAgeVerification = true;
            // Every card spent except the tier celebrations, so no other launch card holds the presenter.
            foreach (var k in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(k)) s.SeenFeatureIntros.Add(k);
            s.SeenFeatureIntros.Remove(TierCelebration.KeyT1);
            s.SeenFeatureIntros.Remove(TierCelebration.KeyT2);
            s.SeenFeatureIntros.Remove(TierCelebration.LegacyKey);
            bool premium = premiumAtLaunch, lab = false;
            void Grant(bool p, bool l) { premium = p; lab = l; }
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            var opened = new List<Window>();
            using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
            MainShellWindow? shell = null;
            try
            {
                Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
                // After the seed, which installs its own providers; the grant is this test's switch.
                CoreAccount.HasPremiumAccessProvider = () => premium;
                CoreAccount.HasLabAccessProvider = () => lab;
                shell = new MainShellWindow();
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, opened, Grant);
            }
            finally
            {
                foreach (var w in opened.Where(w => w != shell).ToArray()) w.Close();
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
                CoreSettings.ServiceProvider = oldSettings;
                AccountSeed.Seed(p => new ProviderSubscription(p));
                CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
                CoreAccount.DisplayNameProvider = null;
                CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
                CoreProgram.HasPremiumProvider = null;
                StartupLadder.ResetForTests();
                FeatureIntroPopup.ResetForTests();
                if (settingsBefore != null) File.WriteAllBytes(settingsPath, settingsBefore); else File.Delete(settingsPath);
            }
        });

    /// <summary>The card, opened now or (in a quiet window) from its Inbox row; null when nothing was offered.</summary>
    private static string? Celebration(List<Window> opened, string key)
    {
        Dispatcher.UIThread.RunJobs();
        if (!opened.OfType<FeatureIntroPopup>().Any()
            && StartupLadder.Inbox.Items.FirstOrDefault(i => i.Key == "intro:" + key) is { } row)
        {
            row.Open!();
            Dispatcher.UIThread.RunJobs();
        }
        var card = opened.OfType<FeatureIntroPopup>().LastOrDefault();
        var title = card?.FindControl<TextBlock>("TxtTitle")?.Text;
        card?.Close();
        opened.Clear();
        Dispatcher.UIThread.RunJobs();
        // The next offer is a fresh one: forget the ladder's one-passive-at-a-time settle (as FeatureIntroWiringTests.ClickNav).
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        return title;
    }

    [Fact]
    public void VoiceGrammarReachesMuteRestoreAndClampedVolumeActions() => Run(false, (shell, opened, grant) =>
    {
        var host = shell.VoiceCmds.Host;
        CoreSettings.Current.MasterVolume = 43;
        host.ActionFor("mute")!();
        Assert.Equal(0, CoreSettings.Current.MasterVolume);
        Assert.True(CoreSettings.Current.AvatarMuted);
        host.ActionFor("mute")!();
        host.ActionFor("unmute")!();
        Assert.Equal(43, CoreSettings.Current.MasterVolume);
        Assert.True(CoreSettings.Current.SubAudioEnabled);
        shell.SetMasterVolume(98);
        host.ActionFor("louder")!();
        Assert.Equal(100, CoreSettings.Current.MasterVolume);
        shell.SetMasterVolume(2);
        host.ActionFor("quieter")!();
        Assert.Equal(0, CoreSettings.Current.MasterVolume);
    });

    [Fact]
    public void TierChangeRepaintsEffectiveAiPermissionWithoutDestroyingSavedChoice() => Run(false, (shell, opened, grant) =>
    {
        CoreSettings.Current.CompanionPrompt.AllowAiToControlEffects = true;
        var permissions = new ConditioningControlPanel.Avalonia.Views.Controls.Companion.AiPermissionsGrid();
        var window = new Window { Content = permissions };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = permissions.FindControl<CheckBox>("ChkCapEffects")!;
            Assert.False(toggle.IsChecked == true);
            grant(true, true);
            AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level2);
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked == true);
            grant(false, false);
            AccountSeed.SubscribeStar!.NotifyEntitlementRaised((PatreonTier)0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked == true);
            Assert.True(CoreSettings.Current.CompanionPrompt.AllowAiToControlEffects);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LiveRiseIgnoresLegacyFlagAndFirstLaunchQuietButBusyRiseStaysInInbox() => Run(false, (shell, opened, grant) =>
    {
        Celebration(opened, TierCelebration.KeyT1);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.SeenFeatureIntros.Add(TierCelebration.LegacyKey);
        CoreSettings.Current.PatreonTier = 0;
        CoreSettings.Current.UnifiedId = "test-account";
        CoreSettings.Current.AuthToken = "test-token";
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        grant(true, false);
        V2AuthService.TierReadingProvider!("test-account", 1);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(opened, w => w is FeatureIntroPopup);
        var ring = shell.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("ProfileBubbleGlowRing")!;
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(0xFF, 0xD7, 0x00),
            Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(ring.Stroke).Color);
        Assert.Contains(TierCelebration.KeyT1, CoreSettings.Current.SeenFeatureIntros);
        Celebration(opened, TierCelebration.KeyT1);

        MainShellWindow.IsStartupDialogShowing = true;
        try
        {
            grant(true, true);
            V2AuthService.TierReadingProvider!("test-account", 2);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(opened, w => w is FeatureIntroPopup);
            Assert.Equal(global::Avalonia.Media.Color.FromRgb(0x5E, 0xE6, 0xFF),
                Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(ring.Stroke).Color);
            Assert.Contains(StartupLadder.Inbox.Items, i => i.Key == "intro:" + TierCelebration.KeyT2);
            Assert.DoesNotContain(TierCelebration.KeyT2, CoreSettings.Current.SeenFeatureIntros);
        }
        finally { MainShellWindow.IsStartupDialogShowing = false; }
    });

    [Fact]
    public void ProviderChangesRebuildProgramsAndRepaintTakeoverLock() => Run(false, (shell, opened, grant) =>
    {
        Celebration(opened, TierCelebration.KeyT1);
        var programs = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.ProgramsTabView>("ProgramsTab")!;
        programs.IsVisible = true;
        programs.RefreshPrograms();
        var list = programs.FindControl<ListBox>("ProgramLibraryList")!;
        var before = list.Items.Cast<object>().ToArray();
        Assert.NotEmpty(before);
        var takeover = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.BambiTakeoverTabView>("BambiTakeoverTab")!;
        shell.RefreshTierDependentViews();
        Assert.True(takeover.FindControl<Control>("AutonomyLocked")!.IsVisible);
        grant(true, false);
        AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level1);
        Dispatcher.UIThread.RunJobs();
        Assert.False(takeover.FindControl<Control>("AutonomyLocked")!.IsVisible);
        Assert.True(takeover.FindControl<Control>("AutonomyUnlocked")!.IsVisible);
        Assert.NotSame(before[0], list.Items.Cast<object>().First());
        grant(false, false);
        AccountSeed.SubscribeStar!.NotifyEntitlementRaised((PatreonTier)0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(takeover.FindControl<Control>("AutonomyLocked")!.IsVisible);
    });

    [Fact]
    public void SubscribeStarTierChangeCelebratesEachTierOnce() => Run(premiumAtLaunch: false, (shell, opened, grant) =>
    {
        Assert.Null(Celebration(opened, TierCelebration.KeyT1));   // nothing held at launch

        grant(true, false);
        AccountSeed.SubscribeStar!.NotifyEntitlementRaised(PatreonTier.Level1);
        Assert.Equal(FeatureIntros.All[TierCelebration.KeyT1].Title, Celebration(opened, TierCelebration.KeyT1));
        Assert.Contains(TierCelebration.KeyT1, CoreSettings.Current.SeenFeatureIntros);

        AccountSeed.SubscribeStar!.NotifyEntitlementRaised(PatreonTier.Level1);   // spent: never again
        Assert.Null(Celebration(opened, TierCelebration.KeyT1));

        grant(true, true);   // Basic -> Prime celebrates again, from the Patreon side
        AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level2);
        Assert.Equal(FeatureIntros.All[TierCelebration.KeyT2].Title, Celebration(opened, TierCelebration.KeyT2));
    });

    [Fact]
    public void PremiumHeldAtLaunchIsCelebratedButAnInviteWeekIsNot() => Run(premiumAtLaunch: true, (shell, opened, grant) =>
    {
        Assert.Equal(FeatureIntros.All[TierCelebration.KeyT1].Title, Celebration(opened, TierCelebration.KeyT1));

        CoreSettings.Current.InviteGrantUntil = DateTime.UtcNow.AddDays(3);   // a free trial, not a purchase
        grant(true, true);
        shell.MaybeShowPremiumCelebration();
        Assert.Null(Celebration(opened, TierCelebration.KeyT2));
        Assert.DoesNotContain(TierCelebration.KeyT2, CoreSettings.Current.SeenFeatureIntros);
    });
}
