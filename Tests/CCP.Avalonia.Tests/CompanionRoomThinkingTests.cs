using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionRoomThinkingTests
{
    [Fact]
    public void ThinkingUsesTheSharedClock_AndHidingTheRoomParksIt() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off; // the hero's ambient clock is irrelevant here
        var room = new CompanionRoomView();
        var parent = new Border { Child = room };
        var win = new Window { Content = parent, Width = 1240, Height = 900 };
        try
        {
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var chat = room.FindControl<ChatThresholdView>("ChatZone")!;
            var vm = new ChatThresholdViewModel();
            chat.ViewModel = vm;
            Dispatcher.UIThread.RunJobs();
            var frames = TopLevelFrameSource.For(win);
            int baseline = frames.SubscriberCount;
            vm.IsThinking = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline + 1, frames.SubscriberCount);
            chat.PaintThinking(450);
            Assert.Equal(1, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot1")!.Opacity, 8);
            Assert.Equal(.75, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot2")!.Opacity, 8);
            Assert.Equal(.5, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot3")!.Opacity, 8);
            parent.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, frames.SubscriberCount);
            Assert.True(vm.IsThinking); // parking is not a reply
            Assert.Equal(1, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot1")!.Opacity);
            parent.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline + 1, frames.SubscriberCount);
            vm.IsThinking = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, frames.SubscriberCount);
            parent.IsVisible = false;
            vm.IsThinking = true;
            vm.IsThinking = false; // a reply landed while the tab was hidden
            parent.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, frames.SubscriberCount);
            vm.IsThinking = true;
            Dispatcher.UIThread.RunJobs();
            win.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, frames.SubscriberCount);
        }
        finally
        {
            win.Close();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
