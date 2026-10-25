using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChromeTabTransitionsTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void ShowTabFadesSlidesAndSurvivesRapidReturn(MotionLevel motion) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldClock = BeatLoop.Time;
        CoreSettings.ServiceProvider = () => newSettings;
        var clock = new Clock();
        BeatLoop.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            var s = CoreSettings.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;
            s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            s.MotionLevel = motion;
            shell = new MainShellWindow();
            var presets = shell.Named<Control>("PresetsTab")!;
            var quests = shell.Named<Control>("QuestsTab")!;
            void Step(double t) { clock.Now += TimeSpan.FromSeconds(t).Ticks; BeatLoop.StepAll(); }
            var cardHost = new StackPanel();
            var dimmed = new Border { Opacity = .5 };
            var hidden = new Border { IsVisible = false };
            cardHost.Children.Add(dimmed);
            cardHost.Children.Add(hidden);
            var cards = new Border[9];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = new Border();
                cardHost.Children.Add(cards[i]);
            }
            cards[1].RenderTransform = new ScaleTransform(1.1, 1.1);
            ((ContentControl)presets).Content = new Border { Child = new ScrollViewer { Content = cardHost } };
            shell.ShowTab("presets");
            Assert.Equal(motion == MotionLevel.Off ? 1 : 0, presets.Opacity);
            if (motion == MotionLevel.Full) Assert.NotEqual(0, ((TranslateTransform)presets.RenderTransform!).X);
            if (motion == MotionLevel.Full)
            {
                Assert.Equal(0, cards[0].Opacity);
                Assert.Equal(10, ((TranslateTransform)cards[0].RenderTransform!).Y);
                Step(.05);
                Assert.InRange(cards[0].Opacity, .39, .41);
                Assert.IsType<ScaleTransform>(cards[1].RenderTransform);
                Assert.Equal(1, cards[8].Opacity); // only eight eligible cards
                Assert.Null(cards[8].RenderTransform);
                Assert.Equal(.5, dimmed.Opacity);
                Assert.Null(dimmed.RenderTransform);
                Assert.Null(hidden.RenderTransform);
                Step(.20);
                Assert.InRange(cards[6].Opacity, .08, .10); // capped 240 ms delay
                Assert.Equal(cards[6].Opacity, cards[7].Opacity);
            }
            else
            {
                Assert.Null(cards[0].RenderTransform);
                Step(.25);
            }
            shell.ShowTab("quests");
            Assert.Equal(1, cards[0].Opacity); // cancellation releases stagger-owned values
            if (cards[0].RenderTransform is TranslateTransform rise) Assert.Equal(0, rise.Y);
            Assert.Equal(motion != MotionLevel.Off, presets.IsVisible);
            Assert.Equal(motion == MotionLevel.Off, presets.IsHitTestVisible);
            Step(.05);
            if (motion != MotionLevel.Off) Assert.InRange(presets.Opacity, .49, .51);
            shell.ShowTab("presets"); // return before outgoing fade finishes
            Assert.True(presets.IsHitTestVisible);
            Step(.7);
            Assert.True(presets.IsVisible);
            Assert.False(quests.IsVisible);
            Assert.True(quests.IsHitTestVisible);
            Assert.Equal(1, presets.Opacity);
            if (presets.RenderTransform is TranslateTransform slide) Assert.Equal(0, slide.X);
            shell.ShowTab("settings"); // native browser tab never slides
            Assert.Null(shell.Named<Control>("SettingsTab")!.RenderTransform);
            Step(.7);
            shell.ShowTab("presets");
            s.MotionLevel = MotionLevel.Off;
            Step(.01);
            Assert.Equal(1, presets.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, shell));
        }
        finally
        {
            shell?.Close();
            BeatLoop.Time = oldClock;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private readonly SettingsService newSettings = new();
    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }
}
