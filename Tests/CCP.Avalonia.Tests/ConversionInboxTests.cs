using System;
using System.Collections.Generic;
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
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ConversionInboxTests
{
    private static Task Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.PatreonMember, s.InboxStamps);
        s.UnifiedId = "conversion-sandbox"; s.PatreonTier = 0; s.ServerTierSeen = null;
        s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null; s.PatreonMember = false;
        s.InboxStamps = new Dictionary<string, string>();
        var shell = new MainShellWindow();
        try { body(shell); }
        finally
        {
            InboxNoticeCard.CloseForPanic(); shell.Close();
            (s.UnifiedId, s.PatreonTier, s.ServerTierSeen, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.PatreonMember, s.InboxStamps) = old;
            StartupLadder.ResetForTests(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LapseParksOnceAndQuietChoiceNeverOpensCheckout() => Run(shell =>
    {
        var s = CoreSettings.Current;
        int opened = 0;
        shell.ConversionCheckout = _ => { opened++; return true; };
        s.PatreonTier = 1; s.ServerTierSeen = 0; s.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(2);
        shell.MaybePostConversionRows(); shell.MaybePostConversionRows();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:lapse_warn_3", row.Key);
        Assert.Null(InboxNoticeCard.Open);
        StartupLadder.Inbox.Open(row);
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        var quiet = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Secondary");
        quiet.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(InboxNoticeCard.Open);
        Assert.Equal(0, opened);
        shell.MaybePostConversionRows(); Assert.Empty(StartupLadder.Inbox.Items);
    });

    [Fact]
    public Task LapsePrimaryClosesBeforeOpeningTheMatchingTierCheckout() => Run(shell =>
    {
        var s = CoreSettings.Current;
        s.PatreonTier = 2; s.ServerTierSeen = 1; s.PatreonLabValidUntil = DateTime.UtcNow.AddHours(12);
        string? opened = null;
        shell.ConversionCheckout = url => { Assert.Null(InboxNoticeCard.Open); opened = url; return true; };
        shell.MaybePostConversionRows();
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items));
        var primary = InboxNoticeCard.Open!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27681505", opened);
    });

    [Fact]
    public Task FreeMemberHelloIsOnceEverAndPanicClosesItsCard() => Run(shell =>
    {
        CoreSettings.Current.PatreonMember = true;
        shell.MaybePostConversionRows(); shell.MaybePostConversionRows();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:free_member_hello", row.Key);
        StartupLadder.Inbox.Open(row);
        Assert.NotNull(InboxNoticeCard.Open);
        PanicSurfaces.All.Single(s => s.Id == "inbox-notice").Stop(shell);
        Assert.Null(InboxNoticeCard.Open);
        shell.MaybePostConversionRows(); Assert.Empty(StartupLadder.Inbox.Items);
    });

    [Fact]
    public Task InviteNewsUsesTheSameQuietInboxAndPersistsOneNoticePerClaim() => Run(shell =>
    {
        var mine = new InviteMine(true, new InviteSnapshot(new[] { new InviteSlot("TEST-INVITE", InviteSlotState.Trying, "Ann", 2) },
            DateTime.UtcNow.AddDays(5), 0), 0);
        shell.ApplyInviteTicket(mine); shell.ApplyInviteTicket(mine);
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("invite:code:TEST-INVITE", row.Key);
        Assert.Contains("Ann", row.Title);
        StartupLadder.Inbox.Open(row);
        Assert.NotNull(InboxNoticeCard.Open);
    });
}
