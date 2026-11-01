using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class EmiIntakeRingTests
{
    [Fact]
    public void IntakeDoorTracksLoginWeeklyPassAndPaidAccess()
    {
        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var s = settings.Current;
        var (week, utc) = (s.IntakePassSpentWeek, s.IntakePassSpentUtc);
        bool loggedIn = false, premium = false;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => loggedIn;
            CoreAccount.HasPremiumAccessProvider = () => premium;
            s.IntakePassSpentWeek = ""; s.IntakePassSpentUtc = null;
            var target = Assert.IsType<EmiTarget>(EmiTargets.Find("intake"));
            Assert.True(target.Available);
            Assert.True(target.Locked);                 // signed out
            loggedIn = true;
            Assert.False(target.Locked);                // free weekly pass
            s.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
            Assert.True(target.Locked);                 // spent this week
            premium = true;
            loggedIn = false;
            Assert.False(target.Locked);                // paid tier is unlimited
        }
        finally
        {
            (s.IntakePassSpentWeek, s.IntakePassSpentUtc) = (week, utc);
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
        }
    }

    [Fact]
    public void LockedIntakePickUsesTheLabRefusalAndNeverScores()
    {
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldGatePremium = CoreEntitlement.HasPremiumProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        TierVerdict? denial = null;
        var state = EmiState.Current;
        var usage = new Dictionary<string, int>(state.Usage);
        var score = new Dictionary<string, double>(state.OpenScore);
        var at = new Dictionary<string, long>(state.UsageAt);
        try
        {
            CoreAccount.IsLoggedInProvider = () => false;
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreEntitlement.HasLabProvider = () => false;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = verdict => denial = verdict;
            state.Usage.Remove("intake"); state.OpenScore.Remove("intake"); state.UsageAt.Remove("intake");
            var target = Assert.IsType<EmiTarget>(EmiTargets.Find("intake"));
            Assert.True(target.Locked);
            target.Open();
            Assert.True(denial.HasValue);
            Assert.False(denial.Value.Allowed);
            Assert.Equal(PatreonTier.Level2, denial.Value.Required);
            Assert.False(state.Usage.ContainsKey("intake"));
            Assert.False(state.OpenScore.ContainsKey("intake"));
            Assert.Empty(IntakeHostWindow.Snapshot());
        }
        finally
        {
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreEntitlement.HasLabProvider = oldLab;
            CoreEntitlement.HasPremiumProvider = oldGatePremium;
            CoreEntitlement.ShowDeniedHandler = oldDenied;
            state.Usage.Clear(); foreach (var item in usage) state.Usage[item.Key] = item.Value;
            state.OpenScore.Clear(); foreach (var item in score) state.OpenScore[item.Key] = item.Value;
            state.UsageAt.Clear(); foreach (var item in at) state.UsageAt[item.Key] = item.Value;
        }
    }

    [Fact]
    public Task IntakeLaunchScoresTheComposedRingAndClickOpensTheHost() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldAi = CoreAi.IsAvailableProvider;
        var state = EmiState.Current;
        var pins = state.Pins.ToList();
        var usage = new Dictionary<string, int>(state.Usage);
        var score = new Dictionary<string, double>(state.OpenScore);
        var at = new Dictionary<string, long>(state.UsageAt);
        var (opens, streak) = (state.RingOpens, state.RingIgnoreStreak);
        var s = settings.Current;
        var (week, utc, fullscreen) = (s.IntakePassSpentWeek, s.IntakePassSpentUtc, s.IntakeFullscreen);
        EmiDeskWindow? desk = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAi.IsAvailableProvider = () => false;  // WPF ring calls Launch directly, not the tab's AI gate
            s.IntakePassSpentWeek = ""; s.IntakePassSpentUtc = null; s.IntakeFullscreen = false;
            state.Pins.Clear(); state.Usage.Clear(); state.OpenScore.Clear(); state.UsageAt.Clear();

            GradedIntakeTabView.OpenIntake(null, duckMain: false).Close();
            Assert.Equal(1, state.Usage["intake"]);
            var slot = Assert.Single(EmiSuggester.Compose(EmiTargets.All), x => x.Target.Id == "intake");
            Assert.False(slot.Locked);

            desk = new EmiDeskWindow();
            desk.Show();
            desk.ToggleRing();
            Dispatcher.UIThread.RunJobs();
            Assert.True(desk.RingOpen);
            var ring = (EmiRingWindow)typeof(EmiDeskWindow).GetField("_ring", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(desk)!;
            var card = Assert.Single(ring.Cards, c => c.Tag is EmiRingCard item && item.Id == "intake");
            await Task.Delay(900); // WPF fan completes before the card is clicked
            Dispatcher.UIThread.RunJobs();
            var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), ring);
            Assert.True(point.HasValue);
            ring.MouseMove(point.Value, RawInputModifiers.None);
            ring.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
            ring.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ring.PickedThisOpening);
            var opened = Assert.Single(IntakeHostWindow.Snapshot());
            Assert.Equal(3, state.Usage["intake"]); // host launch plus ring pick, as WPF
            EmiTargets.Find("intake")!.Open();
            Assert.Same(opened, Assert.Single(IntakeHostWindow.Snapshot()));
            Assert.Equal(4, state.Usage["intake"]); // a refocus only counts the ring pick
        }
        finally
        {
            IntakeHostWindow.CloseAllForPanic();
            desk?.ShutDown();
            state.Pins.Clear(); state.Pins.AddRange(pins);
            state.Usage.Clear(); foreach (var item in usage) state.Usage[item.Key] = item.Value;
            state.OpenScore.Clear(); foreach (var item in score) state.OpenScore[item.Key] = item.Value;
            state.UsageAt.Clear(); foreach (var item in at) state.UsageAt[item.Key] = item.Value;
            (state.RingOpens, state.RingIgnoreStreak) = (opens, streak);
            (s.IntakePassSpentWeek, s.IntakePassSpentUtc, s.IntakeFullscreen) = (week, utc, fullscreen);
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreAi.IsAvailableProvider = oldAi;
        }
    });
}
