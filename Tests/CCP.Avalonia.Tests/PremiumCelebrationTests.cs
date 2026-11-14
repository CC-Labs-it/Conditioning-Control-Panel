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
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-patreon / shell-subscribestar: WPF MaybeShowPremiumCelebration, reached from a provider's
/// TierChanged (OnPatreonTierChanged / OnSubscribeStarTierChanged) and from the launch re-check.</summary>
public sealed class PremiumCelebrationTests
{
    private static void Run(bool premiumAtLaunch, Action<MainShellWindow, List<Window>, Action<bool, bool>> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            if (Application.Current is null)
                builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            else builder.AfterSetupCallback(builder); // Register shipped fonts regardless of test order.
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
            s.WhatMovedCardShown = 1;   // the What-moved card is an owned window, so a live rise would count the user busy
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
    public void VoiceMuteStopsCurrentClipWithoutDroppingTheBubble() => Run(false, (shell, opened, grant) =>
    {
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        shell.ShowAvatarTube();
        s.AvatarMuted = false;
        s.CompanionVoiceLinesMuted = false;
        s.MasterVolume = 80;
        var clip = Path.GetTempFileName(); var stopped = 0;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stopped++;
        try
        {
            var tube = shell.Tube!; Assert.NotNull(tube);
            tube.GigglePriority("hi", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            shell.ApplyVoiceMute(true);
            Assert.Equal(1, stopped);
            Assert.True(tube.HasBubbleUp);
            tube.SetWindowMute(true);
            shell.ApplyVoiceMute(false);
            Assert.False(CoreSettings.Current.AvatarMuted);
            Assert.Same(tube, shell.Tube);
            Assert.False(tube.IsMuted);
        }
        finally { CoreAudio.PlayStoppableProvider = oldAudio; File.Delete(clip); }
        Assert.Same(oldAudio, CoreAudio.PlayStoppableProvider);
    });
    [Fact]
    public void VoiceGrammarReachesMuteRestoreAndClampedVolumeActions() => Run(false, (shell, opened, grant) =>
    {
        shell.ShowTab("appsettings");
        Dispatcher.UIThread.RunJobs();
        var audio = shell.AppSettingsPage!.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AudioSettingsSection>("SectionAudio")!;
        var slider = audio.FindControl<Slider>("SliderMaster")!;
        var host = shell.VoiceCmds.Host;
        CoreSettings.Current.MasterVolume = 43;
        host.ActionFor("mute")!();
        Assert.Equal(0, CoreSettings.Current.MasterVolume);
        Assert.Equal(0, slider.Value);
        Assert.True(CoreSettings.Current.AvatarMuted);
        host.ActionFor("mute")!();
        host.ActionFor("unmute")!();
        Assert.Equal(43, CoreSettings.Current.MasterVolume);
        Assert.Equal(43, slider.Value);
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
    /// <summary>WPF OnGraceClosed -> UpdateUnlockablesVisibility: a confirmed lower reading raises GraceClosed, never
    /// TierChanged, and the effective AI-effects tick still has to drop (the saved choice stays).</summary>
    [Fact]
    public void GraceCloseRepaintsEffectiveAiPermissionWithoutDestroyingSavedChoice() => Run(false, (shell, opened, grant) =>
    {
        var s = CoreSettings.Current;
        s.CompanionPrompt.AllowAiToControlEffects = true;
        s.UnifiedId = "lapsing-account";
        s.AuthToken = "test-token";
        s.PatreonTier = 2;
        grant(true, true);
        var permissions = new ConditioningControlPanel.Avalonia.Views.Controls.Companion.AiPermissionsGrid();
        var window = new Window { Content = permissions };
        var clock = AccountSeed.UtcNow;
        var start = DateTime.UtcNow;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = permissions.FindControl<CheckBox>("ChkCapEffects")!;
            Assert.True(toggle.IsChecked == true);
            AccountSeed.UtcNow = () => start;
            AccountSeed.ApplyTierReading(0, "heartbeat");
            grant(false, false);
            AccountSeed.UtcNow = () => start.AddSeconds(90);
            AccountSeed.ApplyTierReading(0, "heartbeat");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, s.PatreonTier);                 // the grace closed
            Assert.False(toggle.IsChecked == true);
            Assert.True(s.CompanionPrompt.AllowAiToControlEffects);
        }
        finally { AccountSeed.UtcNow = clock; window.Close(); }
    });
    [Fact]
    public void GraceCloseRepaintsAttachedAccountTier() => Run(false, (shell, opened, grant) =>
    {
        var s = CoreSettings.Current;
        s.UnifiedId = "lapsing-account";
        s.AuthToken = "test-token";
        s.PatreonTier = 2;
        CoreAccount.IsLoggedInProvider = () => true;
        grant(true, true);
        var section = new ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AccountSettingsSection();
        var window = new Window { Content = section };
        var clock = AccountSeed.UtcNow;
        var start = DateTime.UtcNow;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tier = section.FindControl<TextBlock>("TxtAccountSectionTier")!;
            var badge = section.FindControl<Border>("AccountTierBadge")!;
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_patreon_tier_level2"), tier.Text);
            Assert.True(badge.IsVisible);
            AccountSeed.UtcNow = () => start;
            AccountSeed.ApplyTierReading(0, "heartbeat");
            grant(false, false);
            AccountSeed.UtcNow = () => start.AddSeconds(90);
            AccountSeed.ApplyTierReading(0, "heartbeat");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_patreon_tier_connected"), tier.Text);
            Assert.False(badge.IsVisible);
            window.Content = null;
            grant(true, true);
            // A detached section must not respond to later grace-close notifications.
            var closed = (Action)typeof(AccountSeed).GetField("GraceClosed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            closed();
            Dispatcher.UIThread.RunJobs();
            Assert.False(badge.IsVisible);
        }
        finally { AccountSeed.UtcNow = clock; window.Close(); }
    });
    [Theory]
    [InlineData("blink")]
    [InlineData("achievements")]
    [InlineData("room")]
    public void OpenViewsRepaintAndDetachOnEntitlementChanges(string kind) => Run(false, (shell, opened, grant) =>
    {
        var s = CoreSettings.Current;
        s.AiChatEnabled = true;
        s.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        if (kind == "blink")
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            s.BlinkTrainerFolders.Add(Path.GetTempPath());
        }
        CoreAccount.UnifiedUserId = null;
        Control view = kind switch
        {
            "blink" => new ConditioningControlPanel.Avalonia.Views.Tabs.BlinkTrainerTabView(),
            "achievements" => new ConditioningControlPanel.Avalonia.Views.Tabs.AchievementsTabView(),
            _ => new ConditioningControlPanel.Avalonia.Views.Controls.Companion.CompanionRoomView(),
        };
        var window = new Window { Content = view };
        bool Locked() => kind switch
        {
            "blink" => view.FindControl<Control>("BlinkTrainerGate")!.IsVisible,
            "achievements" => view.FindControl<Control>("PatronAchievementsOverlay")!.IsVisible,
            _ => ((ConditioningControlPanel.Avalonia.Views.Controls.Companion.EngineRoomVm)
                view.FindControl<Control>("EngineZone")!.DataContext!).StatusLine == ConditioningControlPanel.Localization.Loc.Get("companion_engine_status_disconnected"),
        };
        void Set(bool premium)
        {
            grant(premium, premium);
            CoreAccount.UnifiedUserId = premium ? "test-user" : null;
        }
        void Grace() => ((Action)typeof(AccountSeed).GetField("GraceClosed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!)();
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(Locked());
            Set(true); AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level2); Dispatcher.UIThread.RunJobs();
            Assert.False(Locked());
            if (kind == "blink")
            {
                Assert.True(view.FindControl<Control>("BlinkTrainerGatedContent")!.IsEnabled);
                Assert.True(view.FindControl<Control>("BlinkTrainerStageActions")!.IsEnabled);
                Assert.True(((ConditioningControlPanel.Avalonia.Views.Tabs.BlinkTrainerTabView)view).LivePreview);
            }
            Set(false); AccountSeed.SubscribeStar!.NotifyEntitlementRaised((PatreonTier)0); Dispatcher.UIThread.RunJobs();
            Assert.True(Locked());
            if (kind == "blink") Assert.False(((ConditioningControlPanel.Avalonia.Views.Tabs.BlinkTrainerTabView)view).LivePreview);
            Set(true); Grace(); Dispatcher.UIThread.RunJobs();
            Assert.False(Locked());
            window.Content = null;
            Set(false); Grace(); AccountSeed.Patreon!.NotifyEntitlementRaised((PatreonTier)0); Dispatcher.UIThread.RunJobs();
            Assert.False(Locked());
        }
        finally { window.Close(); CoreAccount.UnifiedUserId = null; }
    });
    [Fact]
    public void AccountChangeRepaintsViewsWithoutTierEvent() => Run(false, (shell, opened, grant) =>
    {
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        bool loggedIn = false;
        CoreAccount.IsLoggedInProvider = () => loggedIn;
        CoreAccount.UnifiedUserId = null;
        shell.UpdateQuickLoginUI(accountChanged: true);
        var blink = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.BlinkTrainerTabView>("BlinkTrainerTab")!;
        var achievements = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.AchievementsTabView>("AchievementsTab")!;
        var room = shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.CompanionTabView>("CompanionTab")!
            .FindControl<ConditioningControlPanel.Avalonia.Views.Controls.Companion.CompanionRoomView>("Room")!;
        var engine = (ConditioningControlPanel.Avalonia.Views.Controls.Companion.EngineRoomVm)room.FindControl<Control>("EngineZone")!.DataContext!;
        var tier = shell.AppSettingsPage!.SectionAccount.FindControl<TextBlock>("TxtAccountSectionTier")!;
        Assert.True(blink.FindControl<Control>("BlinkTrainerGate")!.IsVisible);
        Assert.True(achievements.FindControl<Control>("PatronAchievementsOverlay")!.IsVisible);
        grant(true, true); loggedIn = true; CoreAccount.UnifiedUserId = "test-user";
        shell.UpdateQuickLoginUI(accountChanged: true);
        Assert.False(blink.FindControl<Control>("BlinkTrainerGate")!.IsVisible);
        Assert.False(achievements.FindControl<Control>("PatronAchievementsOverlay")!.IsVisible);
        Assert.NotEqual(ConditioningControlPanel.Localization.Loc.Get("companion_engine_status_disconnected"), engine.StatusLine);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_patreon_tier_level2"), tier.Text);
        grant(false, false); loggedIn = false; CoreAccount.UnifiedUserId = null;
        shell.UpdateQuickLoginUI(accountChanged: true);
        Assert.True(blink.FindControl<Control>("BlinkTrainerGate")!.IsVisible);
        Assert.True(achievements.FindControl<Control>("PatronAchievementsOverlay")!.IsVisible);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("companion_engine_status_disconnected"), engine.StatusLine);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_login_to_unlock_exclusive_features"), tier.Text);
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
        AccountSeed.ApplyTierReading(1, "test");   // the one tier path: heartbeat, profile load and focus refresh all land here
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
            AccountSeed.ApplyTierReading(2, "test");
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
        shell.RefreshEntitlementVeils();   // every navigation: WPF never rebuilds Programs here
        Assert.Same(before[0], list.Items.Cast<object>().First());
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
    public void ProviderTierChangeRefreshesProfileBubble() => Run(premiumAtLaunch: false, (shell, opened, grant) =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.DisplayNameProvider = () => "Alice Brown";
        shell.RefreshProfileBubble();
        var initials = shell.FindControl<TextBlock>("ProfileBubbleInitials")!;
        Assert.Equal("AB", initials.Text);

        CoreAccount.DisplayNameProvider = () => "Carol Davis";
        AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("CD", initials.Text);

        CoreAccount.DisplayNameProvider = () => "Eve Fox";
        AccountSeed.SubscribeStar!.NotifyEntitlementRaised(PatreonTier.Level1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("EF", initials.Text);
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
