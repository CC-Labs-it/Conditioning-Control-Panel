using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BookSpeechTicksLandsInItsMoodAndStopsOnPanicAndClose(bool avatarMuted) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settingsProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var audio = (CoreAudio.PlayStoppableProvider, CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider);
        var oldBark = BarkHead.Engine;
        var bookmark = EmiState.Current.BookCard;
        var opens = EmiState.Current.CodexOpens;
        var svc = EmiDeskService.Instance;
        var clips = new List<(string Path, float Volume, string Tag)>();
        int ticks = 0, stops = 0;
        CoreAudio.PlayStoppableProvider = (path, volume, tag, _, _) =>
        {
            clips.Add((path, volume, tag));
            return () => stops++;
        };
        CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => { Assert.Equal("emi-vox-tick", tag); ticks++; };
        CoreAudio.IsOutputSuppressedProvider = () => false;
        try
        {
            var s = CoreSettings.Current;
            s.AvatarEnabled = s.EmiDeskEnabled = true;
            s.EmiDeskMuteAvatar = avatarMuted;
            s.EmiDeskMuteDontAsk = true;
            s.MasterVolume = 37;
            s.MotionLevel = MotionLevel.Off;
            svc.ResetMutePrompt();
            await svc.Summon();
            var owner = Assert.IsType<EmiDeskWindow>(svc.Window);
            Assert.Equal(avatarMuted, svc.AvatarMuted);
            var rule = new BarkRule { Id = "book-test", Trigger = "T", VariantPool = new() { new BarkVariant("avatar") } };
            var bark = BarkHead.Engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => { } };
            bark.Start();
            var card = EmiBookCards.All.First(c => !string.IsNullOrWhiteSpace(c.MarginEn));
            owner.OpenBook(card.Id);
            // The floor is spent on the first dot, not when the words land. Muting skips the stamp.
            Assert.Equal(avatarMuted, bark.Raise("T"));
            Assert.Equal(1, ticks);
            Assert.Empty(clips);
            for (int i = 0; i < 3; i++) Assert.True(owner.StepSpeech());
            Assert.Equal(3, ticks);
            var clip = Assert.Single(clips);
            Assert.Equal((.37f, "emi-vox"), (clip.Volume, clip.Tag));
            Assert.Equal(EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore(card.MarginEn,
                EmiSpeech.FrameForFace(card.MarginFace)))), File.ReadAllBytes(clip.Path));
            PanicSurfaces.All.Single(p => p.Id == "emi-speech").Stop(null);
            Assert.Equal(1, stops);
            Assert.False(owner.StepSpeech());
            owner.OpenBook(card.Id);
            for (int i = 0; i < 3; i++) Assert.True(owner.StepSpeech());
            Assert.Equal(2, clips.Count);
            owner.BeginPresentation(() => { });
            Assert.Equal(2, stops);
            owner.EndPresentation();
            owner.OpenBook(card.Id);
            for (int i = 0; i < 3; i++) Assert.True(owner.StepSpeech());
            owner.ShutDown();
            Assert.Equal(3, stops);
        }
        finally
        {
            svc.Window?.ShutDown(); svc.ResetMutePrompt();
            (CoreAudio.PlayStoppableProvider, CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider) = audio;
            BarkHead.Engine = oldBark;
            EmiState.Current.BookCard = bookmark; EmiState.Current.CodexOpens = opens;
            CoreSettings.ServiceProvider = settingsProvider;
        }
    });

    [Fact]
    public Task PassiveRecipeRunsBeforeTheBookIsShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var apply = EmiBookWindow.ApplyPassive;
        var bookmark = EmiState.Current.BookCard;
        var book = new EmiBookWindow();
        int applied = 0;
        EmiBookWindow.ApplyPassive = w => { Assert.Same(book, w); Assert.False(w.IsVisible); applied++; };
        try
        {
            book.OpenBook(null);
            Assert.True(book.IsVisible);
            Assert.Equal(1, applied);
            book.OpenBook(null);
            Assert.Equal(1, applied);
        }
        finally { book.Close(); EmiBookWindow.ApplyPassive = apply; EmiState.Current.BookCard = bookmark; }
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
