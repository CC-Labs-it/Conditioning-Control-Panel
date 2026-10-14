using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionRoomPreviewTests
{
    private static CompanionRoomPreviewWindow Create()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return new CompanionRoomPreviewWindow();
    }

    [Fact]
    public Task StateButtonsSwapChatAndMemoryInsteadOfOnlyTheLabel() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            var diary = window.RoomView.GetLogicalDescendants().OfType<MemoryDiaryView>().Single();
            Assert.Equal(CompanionZoneState.Live, chat.ViewModel!.State);
            Assert.NotEmpty(chat.ViewModel.Turns);
            Assert.False(diary.ViewModel!.IsEmpty);
            var button = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "disabled"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("disabled", window.CurrentVariantKey);
            Assert.Equal(CompanionZoneState.Disabled, chat.ViewModel!.State);
            Assert.False(chat.ViewModel.CanSend);
            Assert.Empty(chat.ViewModel.Turns);
            Assert.True(window.ShowVariant("freeTier"));
            Assert.Equal(CompanionZoneState.Locked, chat.ViewModel.State);
            Assert.NotEmpty(chat.ViewModel.TeaserTurns);
            Assert.False(diary.ViewModel!.IsEmpty); // The diary is never paywalled.
            Assert.True(window.ShowVariant("empty"));
            Assert.Equal(CompanionZoneState.Live, chat.ViewModel.State);
            Assert.Empty(chat.ViewModel.Turns);
            Assert.True(diary.ViewModel!.IsEmpty);
            Assert.Equal(new[] { "Level 1", "Streak 1 day", "1 session" }, diary.ViewModel.ProfileStats);
            Assert.True(window.ShowVariant("dormant"));
            Assert.Equal(CompanionZoneState.Dormant, chat.ViewModel.State);
            Assert.False(Assert.Single(chat.ViewModel.Turns).IsAiGenerated);
            Assert.Equal(2, diary.ViewModel!.Facts.Count);
            Assert.True(diary.ViewModel.Facts.Any(f => f.IsDormant));
            Assert.True(window.ShowVariant("default"));
            Assert.NotEmpty(chat.ViewModel.Turns);
            Assert.False(diary.ViewModel!.IsEmpty);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidKeyPreservesThePageAndValidKeysCreateFreshMocks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            Assert.True(window.ShowVariant("EMPTY"));
            var previous = chat.ViewModel!;
            Assert.False(window.ShowVariant(null));
            Assert.False(window.ShowVariant("typo"));
            Assert.Same(previous, chat.ViewModel);
            Assert.Equal("EMPTY", window.CurrentVariantKey);
            previous.Draft = "preview only";
            previous.SendCommand.Execute(null);
            Assert.Single(previous.Turns);
            Assert.True(window.ShowVariant("empty"));
            Assert.NotSame(previous, chat.ViewModel);
            Assert.Empty(chat.ViewModel!.Turns);
            Assert.False(chat.ViewModel.IsAttached);
            var widthToggle = window.FindControl<Button>("WidthToggle")!;
            widthToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1000, window.Width);
            widthToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1240, window.Width);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
