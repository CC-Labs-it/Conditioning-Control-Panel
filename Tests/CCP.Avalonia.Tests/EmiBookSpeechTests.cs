using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiBookSpeechTests
{
    private static EmiDeskWindow Desk()
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return new EmiDeskWindow();
    }

    [Fact]
    public Task BookNavigationDeliversCardSpeechWithLockedHolds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            EmiState.Current.BookCard = EmiBookCards.All[0].Id;
            desk.Show();
            desk.FindControl<Button>("BtnHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(".", desk.BubbleText);
            Assert.Contains("book.margin." + EmiBookCards.All[0].Id, EmiState.Current.RecentIds);
            Assert.Equal("0_0", desk.Face);
            Assert.Equal(420, desk.SpeechHold.TotalMilliseconds);
            desk.AdvanceSpeech();
            Assert.Equal("..", desk.BubbleText);
            Assert.Equal(420, desk.SpeechHold.TotalMilliseconds);
            desk.AdvanceSpeech();
            Assert.Equal("...", desk.BubbleText);
            Assert.Equal(520, desk.SpeechHold.TotalMilliseconds);
            desk.AdvanceSpeech();
            var card = EmiBookCards.All[0];
            Assert.Equal(card.MarginEn, desk.BubbleText);
            Assert.Equal(card.MarginFace, desk.Face);
            Assert.Equal(EmiChains.SayHoldMs(card.MarginEn), desk.SpeechHold.TotalMilliseconds);
            desk.AdvanceSpeech();
            Assert.Null(desk.BubbleText);
            Assert.Equal(200, desk.SpeechHold.TotalMilliseconds);
            Assert.True(desk.ChainLive);
            desk.AdvanceSpeech();
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.Book!.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(".", desk.BubbleText);
            for (int i = 0; i < 3; i++) desk.AdvanceSpeech();
            Assert.Equal(EmiBookCards.All[1].MarginEn, desk.BubbleText);
        }
        finally { desk.CloseBook(); desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementCancelAndPresentationCannotLeaveStaleWords() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        int done = 0;
        try
        {
            desk.Say("old", done: () => done++);
            desk.AdvanceSpeech();
            desk.Say("new", "^_^", () => done++);
            Assert.Equal(".", desk.BubbleText);
            for (int i = 0; i < 3; i++) desk.AdvanceSpeech();
            Assert.Equal("new", desk.BubbleText);
            desk.CancelChain();
            desk.AdvanceSpeech();
            Assert.Null(desk.BubbleText);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, done);
            desk.Say("finished", done: () => done++);
            for (int i = 0; i < 6; i++) desk.AdvanceSpeech();
            Assert.Equal(1, done);
            desk.BeginPresentation(() => { });
            desk.SpeakBookMargin(EmiBookCards.All[0]);
            Assert.Null(desk.BubbleText);
            Assert.False(desk.ChainLive);
            desk.EndPresentation();
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    // WPF EmiDeskWindow.xaml.cs:1629: only a live summon may be cut by a right click.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RightClickTogglesRingWithoutCuttingSpeech(bool ringOpen) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        int done = 0;
        try
        {
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            if (ringOpen) desk.ToggleRing();
            Assert.Equal(ringOpen, desk.RingOpen);
            desk.Say("still reading", "^_^", () => done++);
            for (int i = 0; i < 3; i++) desk.AdvanceSpeech();
            var timerField = typeof(EmiDeskWindow).GetField("_speechTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var timer = (DispatcherTimer)timerField.GetValue(desk)!;
            var hold = desk.SpeechHold;
            var body = desk.FindControl<Control>("BodyRoot")!;
            var at = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
            desk.MouseDown(at, MouseButton.Right);
            desk.MouseUp(at, MouseButton.Right);
            Assert.Equal(!ringOpen, desk.RingOpen);
            Assert.Same(timer, timerField.GetValue(desk));
            Assert.True(timer.IsEnabled);
            Assert.True(desk.ChainLive);
            Assert.Equal("still reading", desk.BubbleText);
            Assert.Equal(hold, desk.SpeechHold);
            Assert.Equal(0, done);
            desk.AdvanceSpeech();
            desk.AdvanceSpeech();
            Assert.False(timer.IsEnabled);
            Assert.Equal(1, done);
            desk.AdvanceSpeech();
            Assert.Equal(1, done);
        }
        finally { desk.CloseRing(); desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DispatcherTimerAdvancesAndCloseStopsSpeech() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var desk = Desk();
        try
        {
            desk.Say("timer");
            var until = DateTime.UtcNow.AddSeconds(5);
            while (desk.BubbleText == "." && DateTime.UtcNow < until)
                await Task.Delay(20);
            Assert.Equal("..", desk.BubbleText);
            desk.Close();
            Assert.False(desk.ChainLive);
            desk.AdvanceSpeech();
            Assert.Null(desk.BubbleText);
        }
        finally { desk.Close(); }
    });

    [Fact]
    public Task BubbleDodgesBookAndReturnsWhenBookCloses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            desk.Position = new PixelPoint(100, 300);
            desk.Show();
            desk.Say("margin");
            var bubble = desk.BubbleHost.Children.OfType<Border>().Single();
            double normal = Canvas.GetLeft(bubble);
            desk.OpenBook(EmiBookCards.All[0].Id);
            int side = desk.Book!.SideOfHer;
            double dodged = Canvas.GetLeft(bubble);
            Assert.Equal(1, side);
            Assert.True(dodged < normal);
            Assert.False(bubble.IsHitTestVisible);
            desk.CloseBook();
            Assert.Equal(normal, Canvas.GetLeft(bubble));
            desk.CancelChain();
            Assert.False(bubble.IsVisible);
        }
        finally { desk.CloseBook(); desk.Close(); }
        return Task.CompletedTask;
    });
}
