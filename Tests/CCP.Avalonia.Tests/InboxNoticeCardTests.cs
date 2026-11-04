using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InboxNoticeCardTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        try { body(); }
        finally { InboxNoticeCard.CloseForPanic(); StartupLadder.ResetForTests(); }
    });
    private static Button FindButton(string name) => InboxNoticeCard.Current!.GetVisualDescendants()
        .OfType<Button>().Single(b => b.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static MainShellWindow NewShell()
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var shell = new MainShellWindow(); // app windows are closed by the isolation hook (TestIsolation.cs)
        shell.Show();
        return shell;
    }
    private static MainShellWindow ShowAndFocusShell() { var shell = NewShell(); shell.Activate(); return shell; }

    private static string? _token;
    /// <summary>A signed-in tier-0 account whose first celebration is spent, with a swappable auth token.</summary>
    private static ConditioningControlPanel.Models.AppSettings SignIn(string token)
    {
        _token = token;
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? _token : null;
        CoreSecrets.StoreProvider = (key, value) => { if (key == CoreSecrets.AuthToken) _token = value; };
        var s = CoreSettings.Current;
        (s.UnifiedId, s.OfflineMode, s.PatreonTier, s.LastKnownEffectiveTier) = (Guid.NewGuid().ToString(), false, 0, 0);
        s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
        s.SeenFeatureIntros.Add(TierCelebration.KeyT2);
        return s;
    }

    private sealed class TierReply(string body, HttpStatusCode status) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task FocusRefreshesServerRiseAndThrottlesRepeatedActivation()
    {
        Run(() => { }); // Initialize the shared dispatcher and shipped fonts.
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var s = SignIn("focus-token");
            s.UnifiedId = null;
            var wire = new TierReply("{\"user\":{\"effective_tier\":2}}", HttpStatusCode.OK);
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            var now = DateTime.UtcNow;
            AccountSeed.FocusClock = () => now;
            AccountSeed.LastFocusCheckUtc = DateTime.MinValue;
            Assert.True(AccountSeed.Seed());
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AccountSeed.Patreon!.TierChanged += (_, _) => changed.TrySetResult();
            var shell = NewShell();
            var other = new Window();
            try
            {
                other.Show();
                s.UnifiedId = Guid.NewGuid().ToString();
                CoreAccount.UnifiedUserId = s.UnifiedId;
                other.Activate();
                shell.Activate(); // User returns after the server changed, with no heartbeat.
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, Volatile.Read(ref wire.Calls));
                Assert.Equal(2, s.PatreonTier);
                Assert.Single(StartupLadder.Inbox.Items, item => item.Key == "tier-on:2");
                other.Activate();
                shell.Activate();
                await Task.Delay(250);
                Assert.Equal(1, Volatile.Read(ref wire.Calls)); // A second focus inside 20 seconds is suppressed.
                _token = "rotated-focus-token";
                now = now.AddSeconds(21);
                other.Activate();
                shell.Activate();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!AccountSeed.ServerTierAnsweredThisRun && DateTime.UtcNow < deadline) await Task.Delay(20);
                Assert.True(AccountSeed.ServerTierAnsweredThisRun);
                Assert.Equal(2, Volatile.Read(ref wire.Calls)); // A later activation reads again.
            }
            finally { other.Close(); shell.Close(); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyFlagCelebratesOnlyAnActualLiveRise(bool rise) => Run(() =>
    {
        var s = SignIn("legacy-token");
        s.SeenFeatureIntros.RemoveAll(key => key == TierCelebration.KeyT2);
        s.SeenFeatureIntros.Add(TierCelebration.LegacyKey);
        s.PatreonTier = rise ? 0 : 2;
        var account = s.UnifiedId;
        s.UnifiedId = null; // Opening the shell must not start an unrelated focus fetch.
        CoreAccount.UnifiedUserId = null;
        Assert.True(AccountSeed.Seed());
        NewShell();
        s.UnifiedId = account;
        CoreAccount.UnifiedUserId = account;
        var sync = new SyncPush(() => null, () => false, new TierReply("{\"effective_tier\":2}", HttpStatusCode.OK))
        { TierReceived = (tier, id, token) => AccountSeed.OfferHeartbeatTier(tier, id, token) };
        Assert.True(sync.HeartbeatAsync().GetAwaiter().GetResult());
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(rise, StartupLadder.Inbox.Items.Any(item => item.Key == "intro:" + TierCelebration.KeyT2));
        Assert.DoesNotContain(StartupLadder.Inbox.Items, item => item.Key.StartsWith("tier-on:"));
        Assert.Equal(2, s.LastKnownEffectiveTier);
        Assert.DoesNotContain(TierCelebration.KeyT2, s.SeenFeatureIntros); // Spent only when opened.
    });

    [Fact]
    public void HeartbeatRiseAppliesAndPostsRepeatNotice() => Run(() =>
    {
        var s = SignIn("old-token");
        Assert.True(AccountSeed.Seed());
        CoreAccount.UnifiedUserId = s.UnifiedId;
        NewShell();
        var (notifications, onUi) = (0, false);
        AccountSeed.Patreon!.TierChanged += (_, _) => (notifications, onUi) = (notifications + 1, Dispatcher.UIThread.CheckAccess());
        string? rawBody = null;
        void Heartbeat() => Assert.True(new SyncPush(() => null, () => false, new TierReply("{\"effective_tier\":2}", HttpStatusCode.OK))
            { TierReceived = (tier, account, token) => AccountSeed.OfferHeartbeatTier(tier, account, token), HeartbeatBody = body => rawBody = body }
            .HeartbeatAsync().GetAwaiter().GetResult());
        Heartbeat();
        _token = "new-token"; // the queued response belongs to the previous session
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, s.PatreonTier);
        Assert.Equal(0, notifications);
        Assert.DoesNotContain(StartupLadder.Inbox.Items, item => item.Key == "tier-on:2");
        Heartbeat();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("{\"effective_tier\":2}", rawBody);
        Assert.True(onUi);
        Assert.Equal(1, notifications);
        Assert.Equal(2, s.PatreonTier);
        Assert.True(CoreAccount.HasLabAccess);
        Assert.True(s.PatreonLabValidUntil > DateTime.UtcNow);
        Assert.Equal(2, s.LastKnownEffectiveTier);
        Assert.Single(StartupLadder.Inbox.Items, item => item.Key == "tier-on:2");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedDropRearmsNoticeButCachedDropDoesNot(bool profile) => Run(() =>
    {
        var s = SignIn("notice-token");
        AccountSeed.Sync = null;
        CoreAccount.IsLoggedInProvider = () => true;
        bool premium = false;
        CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = () => premium;
        s.LastKnownEffectiveTier = 2;
        s.PatreonTier = 2;
        s.PatreonLabValidUntil = s.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(-1);
        var grace = s.PatreonLabValidUntil;
        var shell = NewShell();
        shell.TierOnNoticePass(true);
        Assert.False(AccountSeed.ServerTierAnsweredThisRun);
        Assert.Equal(2, s.LastKnownEffectiveTier); // cached drop is not authoritative
        foreach (var body in new[] { "{}", "{\"effective_tier\":\"0\"}", "{\"effective_tier\":9}", "bad json" })
            Assert.False(Answered(body, HttpStatusCode.OK));
        Assert.False(Answered("{\"effective_tier\":0}", HttpStatusCode.InternalServerError));
        Assert.True(Answered("{\"effective_tier\":0}", HttpStatusCode.OK));
        shell.TierOnNoticePass(true);
        Assert.Equal(0, s.LastKnownEffectiveTier);
        Assert.Equal(2, s.PatreonTier);
        Assert.Equal(grace, s.PatreonLabValidUntil);
        premium = true;
        shell.TierOnNoticePass(true);
        shell.TierOnNoticePass(true);
        Assert.Single(StartupLadder.Inbox.Items, item => item.Key == "tier-on:2");
        Assert.Equal(2, s.LastKnownEffectiveTier);
        _token = "rotated";
        Assert.False(AccountSeed.ServerTierAnsweredThisRun);
        AccountSeed.NoteTierReading(0, s.UnifiedId, "notice-token"); // late response from old token
        Assert.False(AccountSeed.ServerTierAnsweredThisRun);
        s.UnifiedId = "another-account";
        Assert.False(AccountSeed.ServerTierAnsweredThisRun);
        _token = null;
        Assert.False(Answered("{\"effective_tier\":0}", HttpStatusCode.OK));

        bool Answered(string body, HttpStatusCode status)
        {
            var wire = new TierReply(profile ? "{\"user\":" + body + "}" : body, status);
            if (profile) AccountSeed.LoadProfileAsync(new V2AuthService(() => s, wire)).GetAwaiter().GetResult();
            else new SyncPush(() => null, () => false, wire) { TierReceived = (t, a, k) => AccountSeed.NoteTierReading(t, a, k) }
                .HeartbeatAsync().GetAwaiter().GetResult();
            return AccountSeed.ServerTierAnsweredThisRun;
        }
    });

    [Theory]
    [InlineData("", false, false)]
    [InlineData("?", true, true)]
    public void InboxGlyphKeepsEmptyAndUnmappedData(string glyph, bool visible, bool fallback) => Run(() =>
    {
        StartupLadder.Inbox.File(new ConditioningControlPanel.Services.Startup.InboxItem { Key = "glyph", Glyph = glyph });
        var host = new Window { Content = new InboxFlyout() };
        try
        {
            host.Show();
            var icon = host.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Name == "RowGlyph");
            Assert.Equal(visible, icon.IsVisible);
            Assert.Equal(fallback, icon.IsFallback);
            Assert.Equal(glyph, icon.Emoji);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void VisibleOwnerIsUsedAndHiddenOwnerFallsBack() => Run(() =>
    {
        var owner = new Window();
        try
        {
            InboxNoticeCard.Present(owner, "Title", "Body", null, null);
            Assert.Null(InboxNoticeCard.Current!.Owner);
            owner.Show();
            InboxNoticeCard.Present(owner, "Title", "Body", null, null);
            Assert.Same(owner, InboxNoticeCard.Current!.Owner);
        }
        finally { InboxNoticeCard.CloseForPanic(); owner.Close(); }
    });

    [Fact]
    public void FirstCelebrationSuppressesTheRepeatNotice() => Run(() =>
    {
        CoreAccount.HasPremiumAccessProvider = () => true;
        CoreAccount.HasLabAccessProvider = () => true;
        var s = CoreSettings.Current;
        s.LastKnownEffectiveTier = 0;
        s.SeenFeatureIntros.Remove(TierCelebration.KeyT2);
        s.SeenFeatureIntros.Remove(TierCelebration.LegacyKey);
        ShowAndFocusShell();
        Assert.Contains(StartupLadder.Inbox.Items, i => i.Key == "intro:" + TierCelebration.KeyT2);
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("tier-on:"));
        Assert.Equal(2, s.LastKnownEffectiveTier);
    });

    [Fact]
    public void ReplacementEscapeAndPanicNeverRunActions() => Run(() =>
    {
        var calls = 0;
        void Open() => InboxNoticeCard.Present(null, "Title", " ", "Go", () => calls++, "Later", () => calls++);
        Open();
        var first = InboxNoticeCard.Current!;
        Open();
        Assert.False(first.IsVisible);
        Assert.DoesNotContain(InboxNoticeCard.Current!.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Body");
        InboxNoticeCard.Current.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Null(InboxNoticeCard.Current);
        Open();
        PanicSurfaces.All.Single(s => s.Id == "inbox-notice").Stop(null);
        Assert.Null(InboxNoticeCard.Current);
        PanicSurfaces.All.Single(s => s.Id == "inbox-notice").Stop(null);
        Assert.Equal(0, calls);
    });

    [Fact]
    public void ActionsRunAfterCloseAndFailuresAreContained() => Run(() =>
    {
        var calls = 0;
        void Action() { Assert.Null(InboxNoticeCard.Current); calls++; throw new InvalidOperationException("probe"); }
        InboxNoticeCard.Present(null, "Title", "Body", "Go", Action);
        Click(FindButton("Primary"));
        Assert.Equal(1, calls);
        InboxNoticeCard.Present(null, "Title", "Body", null, null, "Later", Action);
        Assert.DoesNotContain(InboxNoticeCard.Current!.GetVisualDescendants().OfType<Button>(), b => b.Name == "Primary");
        Click(FindButton("Secondary"));
        Assert.Equal(2, calls);
        InboxNoticeCard.Present(null, "Title", "", null, null);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("vaultgate_close"), ((TextBlock)FindButton("Secondary").Content!).Text);
    });

    [Fact]
    public void RepeatTierRiseOpensNoticeFromRealInbox() => Run(() =>
    {
        CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = () => true;
        CoreSettings.Current.LastKnownEffectiveTier = 0;
        CoreSettings.Current.SeenFeatureIntros.Add(TierCelebration.KeyT2);
        ShowAndFocusShell().Activate();   // the user path: Opened (celebration re-check) and the Activated focus hook
        Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "tier-on:2");
        var flyout = new InboxFlyout();
        var host = new Window { Content = flyout };
        try
        {
            host.Show();
            var icon = flyout.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Name == "RowGlyph");
            Assert.Equal(("Heart", "Filled", false, true), (icon.Kind.ToString(), icon.Variant.ToString(), icon.IsFallback, icon.IsVisible));
            Click(flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("open")));
            Assert.Empty(StartupLadder.Inbox.Items);
            var title = InboxNoticeCard.Current!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Heading");
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("tier_on_prime"), title.Text);
            Assert.Equal(2, CoreSettings.Current.LastKnownEffectiveTier);
        }
        finally { host.Close(); }
    });

    [Fact]
    public void InviteWeekIsNotARiseOnFocus() => Run(() =>
    {
        var s = CoreSettings.Current;
        CoreAccount.HasPremiumAccessProvider = () => true;
        CoreAccount.HasLabAccessProvider = () => false;
        s.InviteGrantUntil = DateTime.UtcNow.AddDays(5);
        s.PatreonPremiumValidUntil = null;
        s.LastKnownEffectiveTier = 0;
        s.SeenFeatureIntros.Add(TierCelebration.KeyT1);
        ShowAndFocusShell();
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("tier-on:"));
        Assert.Equal(0, s.LastKnownEffectiveTier); // a later real purchase still gets its card
    });

    [Fact]
    public void FirstTierBaselineAndRiseSurviveReloadWithoutClosingTheShell() => Run(() =>
    {
        bool premium = false;
        CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = () => premium;
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        service.Current.Welcomed = service.Current.HasAcceptedAgeVerification = true;
        service.Current.WhatMovedCardShown = 1;
        service.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys.Except(service.Current.SeenFeatureIntros));
        CoreSettings.ServiceProvider = () => service; // restored, and its pending save disarmed, by the isolation hook
        var shell = NewShell();
        var other = new Window();
        try
        {
            other.Show();
            void Refocus()
            {
                SettingsService.DisarmAllPendingSaves();
                other.Activate(); Dispatcher.UIThread.RunJobs();
                shell.Activate(); Dispatcher.UIThread.RunJobs();
            }
            void Reload(int tier)
            {
                Assert.True(service.HasPendingSave); // the notice must queue the write before this flush
                service.SaveImmediate(suppressCloudBackup: true);
                service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
                Assert.Equal(tier, service.Current.LastKnownEffectiveTier);
            }
            SettingsService.DisarmAllPendingSaves();
            service.Current.LastKnownEffectiveTier = null;
            shell.TierOnNoticePass(mayShow: false);
            Reload(0); // reload while both windows remain open, without an exit save
            Refocus();
            Assert.False(service.HasPendingSave); // an unchanged baseline does not write again
            premium = true;
            Assert.Contains(TierCelebration.KeyT2, service.Current.SeenFeatureIntros);
            Refocus();
            Reload(2);
            Refocus();
            Assert.False(service.HasPendingSave);
        }
        finally { other.Close(); }
    });
}
