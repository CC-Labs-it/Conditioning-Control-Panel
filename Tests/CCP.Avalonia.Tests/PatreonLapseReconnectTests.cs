using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PatreonLapseReconnectTests
{
    [Fact]
    public void LapseStopsTakeoverAndResyncsCompanion() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            s.AwarenessModeEnabled = s.AwarenessConsentGiven = true;
            shell.SyncAwareness();
            shell.SyncHero();
            var dial = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().First().ViewModel!;
            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().First().ViewModel!;
            Assert.NotEqual(AwarenessIntensity.Off, dial.Intensity);
            Assert.True(hero.IsAwarenessOpen);
            Assert.True(shell.Autonomy.Start(withTimer: false));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.TakeoverQuestTickRunning);
            CoreEntitlement.HasPremiumProvider = () => false;
            shell.RefreshEntitlementVeils();
            Assert.False(shell.Autonomy.IsEnabled);
            Assert.False(shell.TakeoverQuestTickRunning);
            Assert.False(s.AutonomyModeEnabled);
            Assert.Equal(AwarenessIntensity.Off, dial.Intensity);
            Assert.False(hero.IsAwarenessOpen);
        }
        finally
        {
            shell.Close();
            (CoreSettings.ServiceProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = (oldSettings, oldPremium, oldFree);
        }
    });

    [Fact]
    public void GateReconnectOpensAccountAndRefusesAnInFlightLink() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = TierGate.ReconnectIsTheAnswerProvider;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.AppSettingsPage!;
            var account = page.SectionAccount;
            var button = account.FindControl<Button>("BtnLinkPatreon")!;
            var calls = 0;
            account.ReconnectPatreonOverride = () => { calls++; return Task.CompletedTask; };
            TierGate.ReconnectIsTheAnswerProvider = () => true;
            shell.BtnGateUnlock_Click(null, new RoutedEventArgs());
            Assert.Equal("account", page.CheckedSectionKey);
            Assert.True(page.IsVisible);
            Assert.Equal(1, calls);
            button.IsEnabled = false;
            shell.BtnGateUnlock_Click(null, new RoutedEventArgs());
            Assert.Equal(1, calls);
        }
        finally { TierGate.ReconnectIsTheAnswerProvider = old; shell.Close(); }
    });
}
