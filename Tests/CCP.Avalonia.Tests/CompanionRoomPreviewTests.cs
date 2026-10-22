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
using ConditioningControlPanel.Localization;
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

    // WPF MockChatThresholdVm / MockAttentionGaugeVm copy per strip key, and the per-switch wiring.
    [Fact]
    public Task EachVariantSeatsWpfCopyAttentionAndWiring() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var chat = window.RoomView.GetLogicalDescendants().OfType<ChatThresholdView>().Single();
            var diary = window.RoomView.GetLogicalDescendants().OfType<MemoryDiaryView>().Single();
            var gauge = window.RoomView.GetLogicalDescendants().OfType<AttentionGaugeView>().Single();
            string lastHeard = Loc.GetF("companion_chat_last_heard_fmt", "2h ago");
            var expected = new (string Key, string State, string Footer, string LastHeard, double Fraction)[]
            {
                ("default", "", Loc.Get("companion_chat_footer_remembers"), lastHeard, 0.72),
                ("freeTier", "", "", "", 0.30),
                ("dormant", Loc.Get("companion_chat_dormant_copy"), "", "", 0.72),
                ("empty", "", Loc.Get("companion_chat_footer_first"), "", 0.72),
                ("drained", "", Loc.Get("companion_chat_footer_remembers"), lastHeard, 0.0),
                ("disabled", Loc.Get("companion_chat_disabled_copy"), "", "", 0.72),
            };
            foreach (var (key, state, footer, heard, fraction) in expected)
            {
                diary.ForgetConfirm.ArmCommand.Execute(null);
                Assert.True(diary.ForgetConfirm.IsArmed, key);
                Assert.True(window.ShowVariant(key));
                var vm = chat.ViewModel!;
                Assert.Equal(state, vm.StateCopy);
                Assert.Equal(footer, vm.FooterCopy);
                Assert.Equal(heard, vm.LastHeardCopy);
                Assert.NotNull(vm.OpenEngineRoom); // Loaded never repeats on a strip click
                Assert.True(vm.OpenEngineRoomCommand.CanExecute(null));
                Assert.False(diary.ForgetConfirm.IsArmed, key);
                window.RoomView.SyncBrain(); // a preview gauge never re-reads the live budget
                var a = gauge.ViewModel!;
                Assert.Equal(fraction, a.Fraction, 3);
                Assert.Equal(Loc.Get(AttentionCopy.CopyKeyFor(fraction)), a.StateCopy);
                Assert.Equal(AttentionCopy.ShowUpsell(fraction), a.ShowUpsell);
            }
            Assert.False(gauge.ViewModel!.IsSpent);
            Assert.True(window.ShowVariant("drained"));
            Assert.True(gauge.ViewModel!.IsSpent);
            Assert.Equal(Loc.Get("companion_attention_detail_line_spent"), gauge.ViewModel.DetailLine);
            Assert.True(window.ShowVariant("freeTier"));
            Assert.True(gauge.ViewModel!.ShowUpsell);
            Assert.Equal(Loc.Get("companion_attention_detail_line"), gauge.ViewModel.DetailLine);
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

    [Fact]
    public Task LiveZonesWithoutMocksAreInertInThePreview() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var window = Create();
        try
        {
            var room = window.RoomView.GetLogicalDescendants().ToList();
            // Hero mute, provider, awareness and presets write real settings; no mocks seat them yet.
            Assert.False(room.OfType<CompanionHeroCard>().Single().IsEnabled);
            Assert.False(room.OfType<MakeHerYoursView>().Single().IsEnabled);
            Assert.False(room.OfType<AwarenessPrivacyView>().Single().IsEnabled);
            Assert.False(room.OfType<EngineRoomDrawer>().Single().IsEnabled);
            Assert.False(room.OfType<AiPermissionsGrid>().Single().IsEnabled);
            Assert.False(room.OfType<WorkshopAccordion>().Single().IsEnabled);
            Assert.True(room.OfType<ChatThresholdView>().Single().IsEnabled);
            Assert.True(room.OfType<MemoryDiaryView>().Single().IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
