using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRailHostTests
{
    [Fact]
    public Task MedallionUpdatesAndWithdrawsWithoutARequest() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.DescentSpiralRailEnabled, settings.MotionLevel, settings.PerformanceMode);
        var host = new SpiralRailHost();
        var window = new Window { Width = 100, Height = 100, Content = host };
        try
        {
            settings.DescentSpiralRailEnabled = true;
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(host.IsVisible);
            Assert.Equal(56, host.Height);
            Assert.Equal(new Thickness(0, 6, 0, 2), host.Margin);
            var badge = Assert.IsType<Border>(Assert.Single(host.Children));
            Assert.Equal(40, badge.Width);
            Assert.Equal(1.5, badge.BorderThickness.Left);
            var glyph = Assert.IsType<SpiralGlyph>(badge.Child);
            Assert.Equal(32, glyph.Width);

            var first = new DescentBlock { Stage = new DescentStage { N = 4, NextAt = 100 }, DevotionDays = 50 };
            host.Apply(first, spiralWithheld: true);
            Assert.False(host.IsVisible);
            host.Apply(first, spiralWithheld: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsVisible);
            Assert.True(glyph.IsBreathing);
            Assert.Equal(DescentStageCopy.Name(4) + "\n" + DescentStageCopy.Flavor(4), ToolTip.GetTip(host));
            Assert.Equal("IV", glyph.Children.OfType<TextBlock>().Single().Text);
            if (Environment.GetEnvironmentVariable("CCP_RAIL_RENDER_PATH") is { } proofPath)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(proofPath);
            }

            host.Apply(new DescentBlock { Stage = new DescentStage { N = 7 } }, false);
            Assert.Equal("VII", glyph.Children.OfType<TextBlock>().Single().Text);
            Assert.Equal(DescentStageCopy.Name(7) + "\n" + DescentStageCopy.Flavor(7), ToolTip.GetTip(host));
            settings.DescentSpiralRailEnabled = false;
            host.Arm();
            Assert.False(host.IsVisible);
            Assert.False(glyph.IsBreathing);
            settings.DescentSpiralRailEnabled = true;
            host.Apply(null, false);
            Assert.False(host.IsVisible);
            Assert.False(glyph.IsBreathing);
        }
        finally
        {
            window.Close();
            (settings.DescentSpiralRailEnabled, settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShellMedallionClickOpensTheSpiralRoom() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var old = (settings.DescentSpiralRailEnabled, settings.MotionLevel, settings.PerformanceMode);
        MainShellWindow? shell = null;
        try
        {
            settings.DescentSpiralRailEnabled = true;
            // Pointer routing is independent of ambience; the first fact tests the full-motion clock.
            settings.MotionLevel = MotionLevel.Off;
            settings.PerformanceMode = true;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var rail = shell.Named<SpiralRailHost>("SpiralRail")!;
            Assert.NotNull(rail);
            Assert.False(rail.IsVisible);
            rail.Apply(new DescentBlock { Stage = new DescentStage { N = 3 } }, false);
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.DescentSpiralRailEnabled = false;
            ChasterBillTests.Click(shell, rail);
            Assert.Equal("settings", shell.CurrentTab);
            CoreSettings.Current.DescentSpiralRailEnabled = true;
            ChasterBillTests.Click(shell, rail);
            Assert.True(shell.Named<SpiralTabView>("SpiralTab")!.IsVisible);
            Assert.False(shell.Named<Control>("SettingsTab")!.IsVisible);
        }
        finally
        {
            shell?.Close();
            (settings.DescentSpiralRailEnabled, settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }
}
