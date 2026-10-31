using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

public class InboxNoticeCardTests
{
    private static void WithOwner(Action<Window> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        var owner = new Window();
        try { owner.Show(); test(owner); }
        finally { owner.Close(); StartupLadder.ResetForTests(); }
    });

    [Fact]
    public void NoticeIsOwnedNonmodalAndReplacesPreviousCard() => WithOwner(owner =>
    {
        var first = InboxNoticeCard.Present(owner, "Prime is on", "Everything is open.", null, null);
        InboxNoticeCard? next = null;
        try
        {
            Assert.True(first.IsVisible);
            Assert.Same(owner, first.Owner);
            Assert.False(first.IsDialog);
            next = InboxNoticeCard.Present(owner, "Basic is on", "Everything is open.", null, null);
            Assert.False(first.IsVisible);
            Assert.True(next.IsVisible);
            Assert.False(next.IsDialog);
        }
        finally { first.Close(); next?.Close(); }
    });

    [Fact]
    public void PresenterTracksNoticeAndHoldsNextPassiveUntilItCloses() => WithOwner(owner =>
    {
        InboxNoticeCard? card = null;
        try
        {
            StartupLadder.PresentOrInbox(new InboxItem { Key = "tier-on:2",
                Open = () => card = InboxNoticeCard.Present(owner, "Prime is on", "Everything is open.", null, null) });
            Assert.NotNull(card);
            Assert.True(StartupLadder.PassiveWindowUp());
            int next = 0;
            StartupLadder.PresentOrInbox(new InboxItem { Key = "next-passive", Open = () => next++ });
            var clock = Assert.IsType<StartupLadder.TestClock>(StartupLadder.Time);
            clock.Advance(StartupQueueCore.PassiveSettle + TimeSpan.FromSeconds(2));
            Assert.Equal(0, next);
            card.Close();
            Assert.False(StartupLadder.PassiveWindowUp());
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(1, next);
        }
        finally { card?.Close(); }
    });
}
