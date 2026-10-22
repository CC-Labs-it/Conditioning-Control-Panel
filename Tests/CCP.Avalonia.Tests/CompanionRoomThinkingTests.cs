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
        var time = new SteppedTime();
        global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = time;
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
            // The user path: type a line, press Enter in the draft box (DraftBox_KeyDown -> SendCommand).
            var box = chat.FindControl<TextBox>("DraftBox")!;
            box.Focus();
            box.Text = "hello";
            Dispatcher.UIThread.RunJobs();
            win.KeyPress(global::Avalonia.Input.Key.Enter, global::Avalonia.Input.RawInputModifiers.None,
                global::Avalonia.Input.PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsThinking);
            Assert.Equal(baseline + 1, frames.SubscriberCount);
            // A real window frame paints the dots (not only the explicit seam below).
            var dot1 = chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot1")!;
            dot1.Opacity = 1;
            for (int i = 0; i < 10 && dot1.Opacity == 1; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(dot1.Opacity < 1, $"a frame tick should pulse Dot1, was {dot1.Opacity}");
            chat.PaintThinking(450);
            Assert.Equal(1, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot1")!.Opacity, 8);
            Assert.Equal(.75, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot2")!.Opacity, 8);
            Assert.Equal(.5, chat.FindControl<global::Avalonia.Controls.Shapes.Ellipse>("Dot3")!.Opacity, 8);
            // A minimised window parks the beat (BeatLoop; WPF renders nothing minimised) and a restore wakes it.
            win.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, frames.SubscriberCount);
            win.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline + 1, frames.SubscriberCount);
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
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
