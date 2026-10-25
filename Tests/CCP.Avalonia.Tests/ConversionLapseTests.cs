using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private static Task RunConversion(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        AccountSeed.Seed(Make); // The fixture owns an empty in-memory secret store.
        var answered = AccountSeed.ServerAnsweredThisRun;
        AccountSeed.ServerAnsweredThisRun = true;
        try { body(); }
        finally
        {
            AccountSeed.ServerAnsweredThisRun = answered;
            InboxNoticeCard.CloseActive(); StartupLadder.ResetForTests(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void LapseRowsWaitWhileAGameOwnsTheScreen()
    {
        var old = PanicSurfaces.All;
        StartupLadder.ResetForTests();
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("game-test", _ => { }, () => true) };
            Assert.True(StartupLadder.IsQuiet);
        }
        finally { PanicSurfaces.All = old; StartupLadder.ResetForTests(); }
    }

    [Fact]
    public Task QuietLapseIsStampedOnceAndOpensNeutralCard() => RunConversion(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.InboxStamps);
        var shell = new MainShellWindow();
        try
        {
            s.PatreonTier = 2; s.ServerTierSeen = 1;
            s.PatreonLabValidUntil = DateTime.UtcNow.AddDays(2);
            s.InboxStamps = new();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            shell.MaybePostConversionRows();
            shell.MaybePostConversionRows();
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("conv:lapse_warn_3", row.Key);
            Assert.Contains("Prime", row.Title);
            Assert.Single(s.InboxStamps);
            Assert.Null(InboxNoticeCard.Open);
            row.Open();
            Assert.NotNull(InboxNoticeCard.Open);
            Assert.True(StartupLadder.PassiveWindowUp());
            PanicSurfaces.All.Single(x => x.Id == "inbox-notice").Stop(null);
            Assert.Null(InboxNoticeCard.Open);
        }
        finally
        {
            shell.Close();
            (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.InboxStamps) = old;
        }
    });

    [Fact]
    public Task LapseWaitsForServerAnswerThenFocusAndProfileLoadPostIt() => RunConversion(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.InboxStamps);
        var shell = new MainShellWindow();
        var other = new Window();
        try
        {
            s.PatreonTier = 2; s.ServerTierSeen = 1;
            s.PatreonLabValidUntil = DateTime.UtcNow.AddDays(2);
            s.InboxStamps = new();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));

            // A lower tier remembered from an earlier run: focus posts nothing until the server answers.
            AccountSeed.ServerAnsweredThisRun = false;
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(StartupLadder.Inbox.Items);
            Assert.Empty(s.InboxStamps);

            // The profile load is the server answering: its event posts the row.
            AccountSeed.ServerAnsweredThisRun = true;
            var loaded = (MulticastDelegate?)typeof(AccountSeed)
                .GetField("ProfileLoaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
            loaded!.DynamicInvoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);

            // Focus returning to the shell posts it too (fresh stamps and inbox).
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.InboxStamps = new();
            other.Show();
            other.Activate();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);
        }
        finally
        {
            other.Close();
            shell.Close();
            (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.InboxStamps) = old;
        }
    });

    [Theory]
    [InlineData(false, "conv:lapse_closed")]
    [InlineData(true, "conv:lapse_reconnect")]
    public Task ClosedRowOrReconnectIsPostedOnlyOnce(bool reconnect, string key) => RunConversion(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.InboxStamps);
        var oldReconnect = TierGate.ReconnectIsTheAnswerProvider;
        var shell = new MainShellWindow();
        try
        {
            s.PatreonTier = 1; s.ServerTierSeen = 0;
            s.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(-1);
            s.InboxStamps = new();
            TierGate.ReconnectIsTheAnswerProvider = () => reconnect;
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            shell.MaybePostConversionRows();
            shell.MaybePostConversionRows();
            Assert.Equal(key, Assert.Single(StartupLadder.Inbox.Items).Key);
            Assert.Single(s.InboxStamps);
        }
        finally
        {
            shell.Close();
            TierGate.ReconnectIsTheAnswerProvider = oldReconnect;
            (s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.InboxStamps) = old;
        }
    });

    [Fact]
    public Task CardReplacesPreviousAndClosesBeforeCallingAction() => RunConversion(() =>
    {
        var called = false;
        InboxNoticeCard.Present(null, "Heading", "Body", null, null);
        var first = InboxNoticeCard.Open!;
        InboxNoticeCard.Present(null, "Next", "Body", "Keep", () =>
        {
            Assert.Null(InboxNoticeCard.Open);
            called = true;
        }, "Leave");
        Assert.False(first.IsVisible);
        var card = InboxNoticeCard.Open!;
        var primary = card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Keep"));
        primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(called);
    });
}
