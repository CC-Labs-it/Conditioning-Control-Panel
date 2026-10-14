using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChromeBannerSheenTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void AnnouncementSweepsAndParksOnTheSharedBeat(MotionLevel motion) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldClock = BeatLoop.Time;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var clock = new Clock();
        BeatLoop.Time = clock;
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.WhatMovedCardShown = 1;
        CoreSettings.Current.MotionLevel = motion;
        CoreSettings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow { Width = 600, Height = 100 };
        // Keep the actual authored banner and shell caller, while excluding unrelated tab
        // renderers from this focused headless animation test.
        var bannerHost = shell.FindControl<Grid>("BannerSheenHost")!;
        ((Panel)bannerHost.Parent!).Children.Remove(bannerHost);
        shell.Content = bannerHost;
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            var band = shell.FindControl<Border>("BannerSheen")!;
            var host = shell.FindControl<Grid>("BannerSheenHost")!;
            var slide = ((TransformGroup)band.RenderTransform!).Children.OfType<TranslateTransform>().Single();
            Assert.True(host.Bounds.Width > 0);
            void Rotate() => typeof(MainShellWindow).GetMethod("BannerRotationTimer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object?[] { null, EventArgs.Empty });
            Rotate();
            clock.Now += TimeSpan.FromSeconds(0.3).Ticks;
            BeatLoop.StepAll();
            if (motion == MotionLevel.Off) Assert.Equal(0, band.Opacity);
            else
            {
                Assert.Equal(0.22, band.Opacity, 5);
                Assert.InRange(slide.X, -band.Width + 1, host.Bounds.Width);
                Assert.False(host.IsHitTestVisible);
                clock.Now += TimeSpan.FromSeconds(0.5).Ticks;
                BeatLoop.StepAll();
                Assert.Equal(0, band.Opacity);
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, band));
                // Ordinary four-second rotations are throttled.
                Rotate();
                clock.Now += TimeSpan.FromSeconds(0.3).Ticks;
                BeatLoop.StepAll();
                Assert.Equal(0, band.Opacity);
                // A genuine new announcement bypasses the ordinary rotation's 20 s gap.
                shell.SetBannerAnnouncement("Another message");
                clock.Now += TimeSpan.FromSeconds(0.3).Ticks;
                BeatLoop.StepAll();
                Assert.Equal(0.22, band.Opacity, 5);
                shell.WindowState = WindowState.Minimized;
                Assert.Equal(0, band.Opacity);
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, band));
            }
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = oldSettings;
            BeatLoop.Time = oldClock;
        }
    });

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero).AddTicks(Now);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
