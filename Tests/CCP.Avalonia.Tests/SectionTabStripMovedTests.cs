using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SectionTabStripMovedTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
    });

    private static void Due(DispatcherTimer timer)
    {
        timer.Interval = TimeSpan.Zero;
        Dispatcher.UIThread.Post(() => { });
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData(MotionLevel.Full, 150)]
    [InlineData(MotionLevel.Reduced, 75)]
    [InlineData(MotionLevel.Off, 0)]
    public void FirstAndRepeatShowsResetBeforeFading_ThenExpire(MotionLevel level, int fadeMs) => Run(() =>
    {
        var strip = new SectionTabStrip { MotionOverride = level };
        var note = strip.MovedNoteHost;
        var resets = new List<bool>();
        note.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.OpacityProperty && Equals(e.NewValue, 0d))
                resets.Add(note.Transitions is null);
        };
        try
        {
            strip.ShowMovedNote("First");
            var first = strip.MovedNoteTimer!;
            Assert.True(first.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(3.2), first.Interval);
            strip.ShowMovedNote("Second");
            var second = strip.MovedNoteTimer!;
            Assert.NotSame(first, second);
            Assert.False(first.IsEnabled);
            Assert.True(second.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(3.2), second.Interval);
            Assert.Equal("Second", strip.MovedNoteText);
            Assert.True(strip.MovedNoteVisible);
            Assert.Equal(1, note.Opacity);
            Assert.Equal(new[] { true, true }, resets);
            if (fadeMs == 0) Assert.Null(note.Transitions);
            else
            {
                var fade = Assert.IsType<DoubleTransition>(Assert.Single(note.Transitions!));
                Assert.Equal(Visual.OpacityProperty, fade.Property);
                Assert.Equal(TimeSpan.FromMilliseconds(fadeMs), fade.Duration);
            }

            Due(second); // the actual timer's hold deadline
            if (level != MotionLevel.Off)
            {
                Assert.True(strip.MovedNoteVisible);
                Assert.Equal(0, note.Opacity);
                Assert.Equal(TimeSpan.FromMilliseconds(300), second.Interval);
                Assert.Equal(TimeSpan.FromMilliseconds(300),
                    Assert.IsType<DoubleTransition>(Assert.Single(note.Transitions!)).Duration);
                strip.ShowMovedNote("During fade");
                Assert.False(second.IsEnabled);
                second = strip.MovedNoteTimer!;
                Assert.Equal(TimeSpan.FromSeconds(3.2), second.Interval);
                Assert.Equal("During fade", strip.MovedNoteText);
                Assert.Equal(1, note.Opacity);
                Assert.Equal(TimeSpan.FromMilliseconds(fadeMs),
                    Assert.IsType<DoubleTransition>(Assert.Single(note.Transitions!)).Duration);
                Due(second);
                Assert.True(strip.MovedNoteVisible);
                Due(second); // fade completion
            }
            Assert.False(strip.MovedNoteVisible);
            Assert.False(second.IsEnabled);
            Assert.Null(strip.MovedNoteTimer);
            Assert.Null(note.Transitions);
            Assert.Equal(0, note.Opacity);
        }
        finally { strip.HideMovedNote(); }
    });

    [Fact]
    public void DetachingStopsExpiryAndClearsAnimationState() => Run(() =>
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Full };
        // Keep the strip collapsed: this tests visual-tree lifetime without font layout.
        var window = new Window { Content = strip };
        try
        {
            window.Show();
            strip.ShowMovedNote("Moved");
            var timer = strip.MovedNoteTimer!;
            Assert.True(timer.IsEnabled);
            window.Content = null;
            Assert.False(timer.IsEnabled);
            Assert.Null(strip.MovedNoteTimer);
            Assert.False(strip.MovedNoteVisible);
            Assert.Null(strip.MovedNoteHost.Transitions);
            Assert.Equal(0, strip.MovedNoteHost.Opacity);
        }
        finally { strip.HideMovedNote(); window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TextUsesSemiboldAndTheCurrentSectionHueAtNinetyPercent(bool checkWeight) => Run(() =>
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, CanOpen = _ => false };
        try
        {
            foreach (var (section, tab, expected) in new[]
            {
                (NavSections.Social, "availablesubjects", Color.FromArgb(230, 0x5F, 0xB0, 0xFF)),
                (NavSections.Studio, "studio", Color.FromArgb(230, 0xFF, 0x69, 0xB4)),
            })
            {
                strip.Show(section, tab);
                strip.ShowMovedNote("Moved");
                var text = Assert.IsType<TextBlock>(strip.MovedNoteHost.Child);
                if (checkWeight) Assert.Equal(FontWeight.SemiBold, text.FontWeight);
                else Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
                Assert.Equal(NavStripRules.Accent(section),
                    Assert.IsAssignableFrom<ISolidColorBrush>(strip.MovedNoteHost.BorderBrush).Color);
            }
        }
        finally { strip.HideMovedNote(); }
    });
}
