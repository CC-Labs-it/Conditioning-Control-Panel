using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiBookSpeechTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task OpeningAndNavigatingSpeakTheCardsMarginAndReaction() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var bookmark = EmiState.Current.BookCard;
        var opens = EmiState.Current.CodexOpens;
        var owner = new EmiDeskWindow();
        try
        {
            var cards = EmiBookCards.All.Where(c => !string.IsNullOrWhiteSpace(c.MarginEn)).Take(2).ToArray();
            owner.OpenBook(cards[0].Id);
            var bubble = Assert.IsType<Border>(owner.BubbleHost.Children.FirstOrDefault(c => c is Border));
            var text = Assert.IsType<TextBlock>(bubble.Child);
            Assert.Equal(".", text.Text);
            Assert.Equal("0_0", owner.Face);
            Assert.False(bubble.IsHitTestVisible);
            StepUntil(owner, () => text.Text == cards[0].MarginEn);
            Assert.Equal(cards[0].MarginFace, owner.Face);
            owner.OpenBook(cards[1].Id);
            Assert.Equal(".", text.Text);
            StepUntil(owner, () => text.Text == cards[1].MarginEn);
            Assert.Equal(cards[1].MarginFace, owner.Face);
            owner.CancelChain();
            Assert.False(bubble.IsVisible);
            Assert.False(owner.ChainLive);
        }
        finally
        {
            owner.CloseBook(); owner.CancelChain(); owner.Close();
            EmiState.Current.BookCard = bookmark; EmiState.Current.CodexOpens = opens;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CancellingSpeechHidesTheBubbleAndNeverRunsCompletion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var owner = new EmiDeskWindow();
        try
        {
            bool done = false;
            owner.Say("hello", done: () => done = true);
            Assert.True(owner.ChainLive);
            var bubble = Assert.IsType<Border>(owner.BubbleHost.Children.FirstOrDefault(c => c is Border));
            owner.CancelChain();
            Assert.False(bubble.IsVisible);
            Assert.False(owner.StepSpeech());
            Assert.False(done);
            Assert.False(bubble.IsVisible);
            Assert.False(owner.ChainLive);
        }
        finally { owner.CancelChain(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BookOpenFlipAndCloseNotifyAndMoveTheLiveBubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var bookmark = EmiState.Current.BookCard;
        var opens = EmiState.Current.CodexOpens;
        var owner = new EmiDeskWindow();
        try
        {
            owner.Show();
            var work = owner.Screens.Primary!.WorkingArea;
            owner.Position = new PixelPoint(work.X - (int)EmiDeskWindow.OverlayPadX + 40, work.Y + 100);
            int notices = 0;
            owner.BookSideChanged += (_, _) => notices++;
            owner.OpenBook();
            Assert.True(notices > 0);
            Assert.Equal(1, owner.BookSide);
            var bubble = Assert.IsType<Border>(owner.BubbleHost.Children.First(c => c is Border));
            Dispatcher.UIThread.RunJobs();
            var leftWhenBookRight = Canvas.GetLeft(bubble);
            int beforeFlip = notices;
            owner.Position = new PixelPoint(work.Right - (int)EmiDeskWindow.OverlayPadX - (int)owner.BodyWidth - 300,
                work.Y + 100);
            // The real drag publishes this custom event on release; a headless Position setter alone does not.
            var moved = (EventHandler)typeof(EmiDeskWindow).GetField("Moved",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
            moved(owner, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, owner.BookSide);
            Assert.True(notices > beforeFlip);
            Assert.True(Canvas.GetLeft(bubble) > leftWhenBookRight);
            int beforeClose = notices;
            owner.Book!.Close();
            Assert.Equal(0, owner.BookSide);
            Assert.True(notices > beforeClose);
            owner.OpenBook();
            beforeClose = notices;
            owner.CloseBook();
            Assert.Equal(0, owner.BookSide);
            Assert.True(notices > beforeClose);
        }
        finally
        {
            owner.CloseBook(); owner.CancelChain(); owner.Close();
            EmiState.Current.BookCard = bookmark; EmiState.Current.CodexOpens = opens;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RegisteredPanicStopsTheRealWidgetsSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var owner = EmiDeskService.Instance.BeginPresentation()!;
        try
        {
            bool completed = false;
            owner.Say("book comment", done: () => completed = true);
            var bubble = Assert.IsType<Border>(owner.BubbleHost.Children.First(c => c is Border));
            PanicSurfaces.All.Single(s => s.Id == "emi-speech").Stop(null);
            Assert.False(owner.ChainLive);
            Assert.False(bubble.IsVisible);
            Assert.False(owner.StepSpeech());
            Assert.False(completed);
            Assert.False(bubble.IsVisible);
        }
        finally { owner.ShutDown(); }
        return Task.CompletedTask;
    });

    // Steps the speech player frame by frame (P08: no real timers); the margin is a handful of frames.
    private static void StepUntil(EmiDeskWindow owner, Func<bool> condition)
    {
        for (int i = 0; i < 64 && !condition(); i++)
            if (!owner.StepSpeech()) break;
        Assert.True(condition());
    }
}
