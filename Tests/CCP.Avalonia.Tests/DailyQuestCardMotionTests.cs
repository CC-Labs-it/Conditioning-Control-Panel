using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF DailyQuestCard.xaml.cs motion: progress tweens through MotionFx.BarFill (0.6 s QuadraticEase-out,
/// a resize snaps), hover is MotionFx.HoverLift (1.02 over 150 ms) plus a gold glow blooming to blur 18 / opacity
/// 0.5 over 180 ms and gone at rest. At MotionLevel.Off the bar and the lift snap and the glow never shows.</summary>
public sealed class DailyQuestCardMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task BarTweensAndHoverLiftsAndGlows_SnappedAtOff(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var clock = new SteppedClock();
        DailyQuestCard.Time = clock;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? w = null;
        try
        {
            CoreSettings.Current.MotionLevel = level;
            bool on = level != MotionLevel.Off;

            var card = new DailyQuestCard();
            w = new Window { Width = 400, Height = 500, Content = card };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var root = card.FindControl<Border>("Root")!;
            var fill = card.FindControl<Border>("Fill")!;
            double Track() => card.FindControl<Border>("Track")!.Bounds.Width;
            double track = Track();
            Assert.True(track > 0);
            void Frames(int ms)
            {
                for (int t = 0; t < ms; t += 16)
                {
                    clock.Now += TimeSpan.FromMilliseconds(16).Ticks;
                    card.Step();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            // First layout seats the sample seat's 3/5 with no tween (WPF: a resize is not progress).
            Assert.Equal(track * 0.6, fill.Width, 1);

            card.Apply(new DailyQuestCardModel { Slot = 0, Name = "Surrender", Current = 4, Target = 5, XpText = "120 XP" });
            if (on)
            {
                Assert.Equal(track * 0.6, fill.Width, 1);                          // starts where it stood
                clock.Now += TimeSpan.FromMilliseconds(300).Ticks;                  // half time, QuadOut: 3/4 of the way
                card.Step();
                Assert.Equal(track * (0.6 + 0.2 * 0.75), fill.Width, 1);
                Frames(400);
            }
            // The new "4 / 5" relays the row, and a resize re-seats the bar against the new track.
            Assert.Equal(Track() * 0.8, fill.Width, 1);

            var scale = (ScaleTransform)root.RenderTransform!;
            Assert.Equal(default, root.BoxShadow);                                  // nothing drawn at rest
            w.MouseMove(new Point(200, 100), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            if (on) Assert.True(scale.ScaleX < 1.02, "motion on should tween the lift, not snap it");
            else Assert.Equal(1.02, scale.ScaleX, 3);
            Frames(240);
            Assert.Equal(1.02, scale.ScaleX, 3);
            Assert.Equal(1.02, scale.ScaleY, 3);
            if (on)
            {
                Assert.Equal(1, root.BoxShadow.Count);
                var glow = root.BoxShadow[0];
                Assert.Equal(18, glow.Blur, 3);
                Assert.Equal(Color.FromArgb(128, 0xFF, 0xD7, 0x00), glow.Color);
            }
            else Assert.Equal(default, root.BoxShadow);

            w.MouseMove(new Point(5, 5), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Frames(240);
            Assert.Equal(1.0, scale.ScaleX, 3);
            Assert.Equal(default, root.BoxShadow);                                  // detached once faded
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            DailyQuestCard.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
