using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Inbox.cs MaybePostLapseRow (restack b160, from #4366): the lapse warning, closed and
/// reconnect rows read only a server answer of this run (AccountSeed.ApplyTierReading), never cached state.</summary>
public sealed class LapseInboxTests
{
    private const string PrimeUrl = "https://www.patreon.com/checkout/CodeBambi?rid=27681505";
    private const string Id = "lapse-test", Token = "token";

    private static readonly PropertyInfo PatreonProp = typeof(AccountSeed).GetProperty("Patreon", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo SubstarProp = typeof(AccountSeed).GetProperty("SubscribeStar", BindingFlags.Static | BindingFlags.NonPublic)!;
    // Private reading state the assembly snapshot does not cover.
    private static readonly FieldInfo[] PrivateState = new[] { typeof(EntitlementRefresh), typeof(CheckoutWatch) }
        .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        .Concat(new[] { typeof(AccountSeed).GetField("Readings", BindingFlags.Static | BindingFlags.NonPublic)! })
        .Where(f => !f.IsInitOnly && !f.IsLiteral).ToArray();

    private static Task Run(Func<MainShellWindow, StartupLadder.TestClock, List<string>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var oldReconnect = TierGate.ReconnectIsTheAnswerProvider;
        var oldOpener = MainShellWindow.PlanOpener;
        var oldUtcNow = AccountSeed.UtcNow;
        var oldPatreon = AccountSeed.Patreon;
        var oldSubstar = AccountSeed.SubscribeStar;
        var oldPrivate = PrivateState.Select(f => f.GetValue(null)).ToArray();
        var oldLifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
        MainShellWindow? shell = null;
        var secrets = new Dictionary<string, string?>();
        try
        {
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var service = new SettingsService();
            var s = service.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            s.WhatMovedCardShown = 1;
            CoreSettings.ServiceProvider = () => service;
            // Offline: the shell's own focus read never leaves the test; readings are fed by hand.
            s.UnifiedId = Id; s.AuthToken = Token; s.OfflineMode = true;
            s.PatreonTier = 2; s.ServerTierSeen = 0;   // a lower reading remembered from an earlier run
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.HasLabAccessProvider = () => false;
            PatreonProp.SetValue(null, new ProviderSubscription("patreon", () => s));
            SubstarProp.SetValue(null, new ProviderSubscription("substar", () => s));
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            var opened = new List<string>();
            MainShellWindow.PlanOpener = url => { opened.Add(url); return true; };
            foreach (var f in PrivateState) f.SetValue(null, f.FieldType == typeof(EntitlementReadings) ? new EntitlementReadings() : f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
            StartupLadder.ResetForTests();
            // The first-launch quiet window: rows are filed, never presented, so the test opens them.
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromDays(2));
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            AccountSeed.UtcNow = () => clock.GetUtcNow().UtcDateTime;
            s.PatreonLabValidUntil = clock.GetUtcNow().UtcDateTime.AddDays(2);
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            lifetimeField.SetValue(Application.Current, lifetime);
            shell = new MainShellWindow();
            lifetime.MainWindow = shell;
            shell.Show();
            await body(shell, clock, opened);
        }
        finally
        {
            try
            {
                InboxNoticeCard.Open?.Close();
                shell?.Close();
                lifetimeField.SetValue(Application.Current, oldLifetime);
                StartupLadder.ResetForTests();
            }
            finally
            {
                for (var i = 0; i < PrivateState.Length; i++) PrivateState[i].SetValue(null, oldPrivate[i]);
                PatreonProp.SetValue(null, oldPatreon);
                SubstarProp.SetValue(null, oldSubstar);
                AccountSeed.UtcNow = oldUtcNow;
                MainShellWindow.PlanOpener = oldOpener;
                TierGate.ReconnectIsTheAnswerProvider = oldReconnect;
                CoreAccount.HasPremiumAccessProvider = oldPremium;
                CoreAccount.HasLabAccessProvider = oldLab;
                CoreSettings.ServiceProvider = oldSettings;
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
            }
        }
    });

    private static Button Primary()
    {
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        return card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoticePrimary");
    }

    // Only this file's rows: the shell files its own startup rows (the tier-on notice and others) beside them.
    private static List<ConditioningControlPanel.Services.Startup.InboxItem> Lapse() =>
        StartupLadder.Inbox.Items.Where(i => i.Key.StartsWith("conv:lapse", StringComparison.Ordinal)).ToList();

    private static string Label(Button b) => ((TextBlock)b.Content!).Text!;

    [Fact]
    public Task CachedLowerTierPostsNothing_ThenALiveLowerReadingWarnsOnceAndOpensPrimeCheckout() => Run((shell, clock, opened) =>
    {
        shell.MaybePostLapseRow();
        Assert.Empty(Lapse());
        Assert.Empty(CoreSettings.Current.InboxStamps);

        AccountSeed.ApplyTierReading(1, "test");   // the first lower reading: grace still open, ServerTierSeen moves
        Dispatcher.UIThread.RunJobs();
        var row = Assert.Single(Lapse());
        Assert.Equal("conv:lapse_warn_3", row.Key);
        Assert.Equal(Loc.GetF("conv_lapse_warn_prime", 2), row.Title);
        Assert.Equal(Loc.Get("conv_lapse_warn_body"), row.Summary);
        Assert.True(IconMap.TryGet(row.Glyph, out var icon));
        Assert.Equal(IconKind.HourglassHalf, icon.Kind);
        Assert.Equal(clock.GetUtcNow().AddDays(2).ToString("yyyy-MM-dd"), CoreSettings.Current.InboxStamps["lapse_warn_3"]);

        shell.MaybePostLapseRow();   // a focus storm posts it once
        Assert.Single(Lapse());

        StartupLadder.Inbox.Open(row);
        var button = Primary();
        Assert.Equal(Loc.Get("conv_keep_prime"), Label(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { PrimeUrl }, opened);
        return Task.CompletedTask;
    });

    [Fact]
    public Task LiveCoverageSuppresses_ThenAnEndedWindowPostsTheClosedRow() => Run((shell, clock, opened) =>
    {
        AccountSeed.Patreon!.SetWhitelistStatus(true);
        AccountSeed.ApplyTierReading(1, "test");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Lapse());
        Assert.Empty(CoreSettings.Current.InboxStamps);

        PatreonProp.SetValue(null, new ProviderSubscription("patreon", () => CoreSettings.Current));
        CoreSettings.Current.PatreonLabValidUntil = clock.GetUtcNow().UtcDateTime.AddHours(-1);
        shell.MaybePostLapseRow();
        var row = Assert.Single(Lapse());
        Assert.Equal("conv:lapse_closed", row.Key);
        Assert.Equal(Loc.Get("conv_lapse_closed_body"), row.Summary);
        StartupLadder.Inbox.Open(row);
        var button = Primary();
        Assert.Equal(Loc.Get("conv_reopen_prime"), Label(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { PrimeUrl }, opened);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ADeadGrantGetsTheReconnectRowOnceADay() => Run((shell, clock, opened) =>
    {
        TierGate.ReconnectIsTheAnswerProvider = () => true;
        var reconnects = 0;
        shell.ReconnectFromGateOverride = () => { reconnects++; return Task.CompletedTask; };
        AccountSeed.ApplyTierReading(1, "test");
        Dispatcher.UIThread.RunJobs();
        var row = Assert.Single(Lapse());
        Assert.Equal("conv:lapse_reconnect", row.Key);
        Assert.Equal(Loc.Get("tiergate_denied_reconnect"), row.Title);
        Assert.True(IconMap.TryGet(row.Glyph, out var icon));
        Assert.Equal(IconKind.Key, icon.Kind);
        Assert.False(CoreSettings.Current.InboxStamps.ContainsKey("lapse_warn_3"));
        shell.MaybePostLapseRow();
        Assert.Single(Lapse());
        StartupLadder.Inbox.Open(row);
        var button = Primary();
        Assert.Equal(Loc.Get("tiergate_reconnect_action"), Label(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, reconnects);
        Assert.Empty(opened);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AnAnswerForAnotherAccountOrAfterLogoutPostsNothing() => Run((shell, clock, opened) =>
    {
        var s = CoreSettings.Current;
        s.OfflineMode = false;   // a heartbeat is only read online; no window activation runs in between
        try
        {
            AccountSeed.ApplyHeartbeatTier("someone-else", Token, "{\"effective_tier\":1}");
            AccountSeed.ApplyHeartbeatTier(Id, "stale-token", "{\"effective_tier\":1}");
            shell.MaybePostLapseRow();
            Assert.Empty(Lapse());
            Assert.Empty(s.InboxStamps);

            AccountSeed.ApplyHeartbeatTier(Id, Token, "{\"effective_tier\":1}");   // a heartbeat answer counts
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("conv:lapse_warn_3", Assert.Single(Lapse()).Key);
        }
        finally { s.OfflineMode = true; }

        s.InboxStamps.Clear();
        s.AuthToken = null;   // signed out: the answer no longer belongs to anyone
        shell.MaybePostLapseRow();
        Assert.Empty(s.InboxStamps);
        return Task.CompletedTask;
    });
}
