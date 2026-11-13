using System;
using System.Linq;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
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
            Assert.Same(owner, first.Owner); Assert.False(first.IsDialog);
            first.Resources["PinkBrush"] = global::Avalonia.Media.Brushes.Cyan; Assert.Same(global::Avalonia.Media.Brushes.Cyan, Assert.IsType<Border>(first.Content).BorderBrush);
            next = InboxNoticeCard.Present(owner, "Basic is on", "Everything is open.", null, null);
            Assert.False(first.IsVisible);
            Assert.True(next.IsVisible);
            Assert.False(next.IsDialog);
        }
        finally { first.Close(); next?.Close(); }
    });

    // Restack b33 (includes #3638): a panic closes the card through its registry row and runs neither action.
    [Fact]
    public void PanicClosesTheCardWithoutRunningItsActions() => WithOwner(owner =>
    {
        int primary = 0, secondary = 0;
        var card = InboxNoticeCard.Present(owner, "Prime is on", "Everything is open.", "Open", () => primary++, "Later", () => secondary++);
        try
        {
            Assert.Same(card, InboxNoticeCard.Open);
            var row = ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(x => x.Id == "inbox-notice");
            row.Stop(null);
            Assert.False(card.IsVisible);
            Assert.Null(InboxNoticeCard.Open);
            Assert.Equal((0, 0), (primary, secondary));
            InboxNoticeCard.CloseForPanic();   // nothing open: harmless
        }
        finally { card.Close(); }
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
    [Fact]
    public void InboxRendersSemanticIconAndPreservesLegacyGlyph() => WithOwner(owner =>
    {
        var symbol = new InboxItem { Key = "symbol", IconKey = "Heart", Title = "Prime is on" };
        var legacy = new InboxItem { Key = "legacy", Glyph = "old", Title = "Legacy" };
        StartupLadder.Inbox.File(symbol);
        StartupLadder.Inbox.File(legacy);
        var flyout = new InboxFlyout();
        owner.Content = flyout;
        owner.Width = 400; owner.Height = 300;
        owner.UpdateLayout();
        var icons = flyout.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Name == "InboxRowIcon").ToArray();
        var heart = Assert.Single(icons, g => ReferenceEquals(g.DataContext, symbol));
        Assert.True(heart.IsVisible);
        Assert.Equal("Heart", heart.Kind.ToString());
        Assert.False(heart.IsFallback);
        Assert.False(Assert.Single(icons, g => ReferenceEquals(g.DataContext, legacy)).IsVisible);
        Assert.Contains(flyout.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "old" && t.IsVisible);
    });

}
