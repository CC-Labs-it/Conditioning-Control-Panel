using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed class SubscribeStarRefreshTests
{
    [Fact]
    public Task TierChangesRepaintPermissionsAndProgramsWithoutLosingSavedChoice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current is null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else builder.AfterSetupCallback(builder); // Same production fonts if another test initialized the platform.
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var originalSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Off;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        foreach (var key in FeatureIntros.All.Keys)
            if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        s.CompanionPrompt ??= new CompanionPromptSettings();
        s.CompanionPrompt.AllowAiToControlEffects = true;
        bool premium = false, lab = false, freeTakeover = false;
        MainShellWindow? shell = null;
        try
        {
            Assert.True(AccountSeed.Seed(prefix => new ProviderSubscription(prefix, () => s)));
            CoreAccount.HasPremiumAccessProvider = () => premium;
            CoreAccount.HasLabAccessProvider = () => lab;
            CoreEntitlement.HasPremiumProvider = () => premium;
            CoreEntitlement.HasLabProvider = () => lab;
            CoreEntitlement.IsFreeTodayProvider = key => key == "takeover" && freeTakeover;
            CoreProgram.HasPremiumProvider = () => premium;
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var programs = shell.Named<ProgramsTabView>("ProgramsTab")!;
            programs.UseProgramLibrary(new[] { new ProgramDefinition { Id = "test-premium", Title = "Premium", Tier = ProgramTier.Premium } });
            var list = programs.FindControl<ListBox>("ProgramLibraryList")!;
            ProgramBrowseItem Item() => ((IEnumerable<ProgramBrowseItem>)list.ItemsSource!).Single();
            var room = shell.Named<CompanionTabView>("CompanionTab")!.FindControl<CompanionRoomView>("Room")!;
            var permissions = room.FindControl<AiPermissionsGrid>("PermissionsZone")!;
            var master = permissions.FindControl<CheckBox>("ChkCapEffects")!;
            var details = permissions.FindControl<Border>("EffectPermsPanel")!;
            var lockband = permissions.FindControl<Grid>("EffectsLockBand")!;
            Assert.True(Item().IsLocked);
            Assert.False(master.IsChecked);
            Assert.False(details.IsVisible);
            Assert.True(lockband.IsVisible);
            var provider = AccountSeed.SubscribeStar!;

            // The event may arrive on a background validation continuation, as in WPF.
            premium = lab = true;
            await Task.Run(() => provider.NotifyEntitlementRaised(PatreonTier.Level2));
            Dispatcher.UIThread.RunJobs();
            Assert.False(Item().IsLocked);
            Assert.True(master.IsChecked);
            Assert.True(details.IsVisible);
            Assert.False(lockband.IsVisible);
            Assert.True(s.CompanionPrompt.AllowAiToControlEffects);

            // Lapse paints the effective OFF value; the saved preference must survive (#1307).
            premium = lab = false;
            provider.NotifyEntitlementRaised(PatreonTier.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Item().IsLocked);
            Assert.False(master.IsChecked);
            Assert.False(details.IsVisible);
            Assert.True(lockband.IsVisible);
            Assert.True(s.CompanionPrompt.AllowAiToControlEffects);

            // A free Takeover day never grants AI effects or premium Programs.
            freeTakeover = true;
            provider.NotifyEntitlementRaised(PatreonTier.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(master.IsChecked);
            Assert.True(Item().IsLocked);
            freeTakeover = false;
            provider.NotifyEntitlementRaised(PatreonTier.None);
            Dispatcher.UIThread.RunJobs();

            // Hidden Programs defer rebuilding until the user returns.
            shell.ShowTab("settings");
            Assert.False(programs.IsVisible);
            var hiddenItems = list.ItemsSource;
            premium = lab = true;
            provider.NotifyEntitlementRaised(PatreonTier.Level2);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(hiddenItems, list.ItemsSource);
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Item().IsLocked);
            premium = lab = false;
            provider.NotifyEntitlementRaised(PatreonTier.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Item().IsLocked);

            // A queued refresh is discarded after close; the event subscription is also removed.
            var beforeClose = list.ItemsSource;
            premium = lab = true;
            provider.NotifyEntitlementRaised(PatreonTier.Level2);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(beforeClose, list.ItemsSource);
            provider.NotifyEntitlementRaised(PatreonTier.Level2);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(beforeClose, list.ItemsSource);
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldSettings;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
            AccountSeed.Seed(prefix => new ProviderSubscription(prefix));
            CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
            CoreAccount.DisplayNameProvider = null;
            CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
            CoreEntitlement.IsFreeTodayProvider = oldFree;
            CoreProgram.HasPremiumProvider = null;
            if (originalSettings != null) File.WriteAllBytes(settingsPath, originalSettings); else if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    });
}
