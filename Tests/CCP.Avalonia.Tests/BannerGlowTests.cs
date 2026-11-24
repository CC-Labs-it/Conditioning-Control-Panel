using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BannerGlowTests
{
    [Fact]
    public void EveryBeatGlowsAndIncomingBeatKeepsGlowDuringDrumAndPop() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.MotionLevel = MotionLevel.Full;
        s.GentleVisuals = false;
        s.PerformanceMode = false;
        s.AutoPerformanceMode = false;
        s.OfflineMode = true;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            var pink = Assert.IsType<Color>(shell.FindResource("PinkColor"));
            void Check(TextBlock beat)
            {
                var effect = Assert.IsType<DropShadowEffect>(beat.Effect);
                Assert.Equal(pink, effect.Color);
                Assert.Equal(8, effect.BlurRadius);
                Assert.Equal(0, effect.OffsetX);
                Assert.Equal(0, effect.OffsetY);
                Assert.Equal(0.6, effect.Opacity);
            }
            foreach (var name in new[] { "TxtBannerPrimary", "TxtBannerSecondary", "TxtBannerWeb", "TxtBannerPool" })
                Check(shell.FindControl<TextBlock>(name)!);
            var beats = (TextBlock[])typeof(MainShellWindow).GetField("_bannerBeats", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            var index = (int)typeof(MainShellWindow).GetField("_bannerCurrentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            var incoming = beats[(index + 1) % beats.Length];
            shell.BannerRotationTimer_Tick(null, EventArgs.Empty);
            var group = Assert.IsType<TransformGroup>(incoming.RenderTransform);
            Assert.IsType<ScaleTransform>(group.Children[0]);
            Assert.IsType<TranslateTransform>(group.Children[1]);
            Check(incoming);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
