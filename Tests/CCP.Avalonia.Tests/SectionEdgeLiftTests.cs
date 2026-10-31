using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SectionEdgeLiftTests
{
    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(0, 1.5, 0)]
    [InlineData(0, 3, 1)]
    [InlineData(1, 1.5, -1)]
    [InlineData(1, 4.5, 0)]
    [InlineData(1, 6, 1)]
    [InlineData(2, 4.5, 1)]
    [InlineData(2, 7.5, 0)]
    [InlineData(2, 9, -1)]
    [InlineData(3, 7.5, 1)]
    [InlineData(3, 10.5, 0)]
    [InlineData(3, 11.999, -0.999333333333333)]
    [InlineData(0, 12, -1)]
    [InlineData(3, -1.5, 0)]
    public void ClockwiseLegWaitsCrossesAndParks(int side, double seconds, double expected) =>
        Assert.Equal(expected, SectionEdgeLift.LegAt(side, seconds), 8);

    [Fact]
    public async Task LiftPausesResumesParksAndStopsOnDetach()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var lift = new SectionEdgeLift();
            var w = new Window { Content = lift };
            try
            {
                w.Show();
                lift.Apply(MotionLevel.Full, true, true);
                Assert.True(lift.IsSpinning);
                lift.Step(1.5);
                Assert.Equal(0, lift.OffsetOf(0));
                lift.Apply(MotionLevel.Full, true, false);
                Assert.False(lift.IsSpinning);
                lift.Apply(MotionLevel.Full, true, true);
                Assert.Equal(0, lift.OffsetOf(0)); // resume keeps its lap position
                lift.Apply(MotionLevel.Full, false, true); // low tier: a fixed lift
                Assert.False(lift.IsSpinning);
                Assert.Equal(0, lift.OffsetOf(0));
                Assert.Equal(-1, lift.OffsetOf(1));
                Assert.Equal(1, lift.OffsetOf(2));
                Assert.Equal(1, lift.OffsetOf(3));
                lift.Apply(MotionLevel.Reduced, true, true);
                Assert.False(lift.IsSpinning);
                Assert.True(lift.IsVisible);
                lift.Apply(MotionLevel.Off, true, true);
                Assert.False(lift.IsVisible);
                lift.Apply(MotionLevel.Full, true, true);
                w.Content = null;
                Assert.False(lift.IsSpinning);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShellRegatesTheLiftWithoutASectionChange()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var s = CoreSettings.Current;
            var oldMotion = s.MotionLevel;
            var oldPerformance = s.PerformanceMode;
            // Keep startup surfaces from owning activation in this shell lifecycle test.
            (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown) = (true, true, 1);
            s.AvatarEnabled = false;
            var w = new MainShellWindow();
            try
            {
                w.Show();
                Dispatcher.UIThread.RunJobs();
                w.ShowTab("presets");
                var lift = w.Named<SectionEdgeLift>("SectionEdgeLift")!;
                s.MotionLevel = MotionLevel.Reduced;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(lift.IsVisible);
                Assert.False(lift.IsSpinning);
                Assert.Equal(0, lift.OffsetOf(0));
                s.MotionLevel = MotionLevel.Off;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.False(lift.IsVisible);
                s.MotionLevel = MotionLevel.Full;
                s.PerformanceMode = true;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(lift.IsVisible);
                Assert.False(lift.IsSpinning);
                // WPF MainWindow.SectionEdge.cs:165: hiding must stop a lap that was running.
                s.PerformanceMode = false;
                w.Activate();
                Dispatcher.UIThread.RunJobs();
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(w.IsActive);
                Assert.True(lift.IsSpinning);
                w.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.False(lift.IsSpinning);
                w.Show();
                w.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.True(w.IsActive);
                Assert.True(lift.IsSpinning);
                w.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.False(lift.IsSpinning);
                w.WindowState = WindowState.Normal;
                w.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.True(lift.IsSpinning);
                // Headless does not deactivate an existing window when another activates.
                // Deliver the platform notification through the shell's real event path.
                var deactivated = typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty("Deactivated",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                Assert.IsType<Action>(deactivated.GetValue(w.PlatformImpl))();
                Assert.True(w.IsVisible);
                Assert.False(w.IsActive);
                Assert.False(lift.IsSpinning);
                w.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.True(w.IsActive);
                Assert.True(lift.IsSpinning);
            }
            finally { w.Close(); s.MotionLevel = oldMotion; s.PerformanceMode = oldPerformance; }
            return Task.CompletedTask;
        });
    }

    private static void SetUp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
