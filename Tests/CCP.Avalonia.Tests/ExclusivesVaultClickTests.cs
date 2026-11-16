using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesVaultClickTests
{
    [Fact]
    public Task CardOpensImmediatelyWithoutParticlesAndAfterBurstWithParticles() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            settings.PerformanceMode = false;
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard");
            var feature = ((ExclusiveCardRow)card.DataContext!).Feature;
            var canvas = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            int opened = 0;
            settings.MotionLevel = MotionLevel.Off;
            view.OnVaultCardClicked(card, feature, () => opened++);
            Assert.Equal(1, opened);
            Assert.Equal(0, canvas.BurstCount);

            settings.MotionLevel = MotionLevel.Full;
            Assert.True(AmbientFxCanvas.Env.AllowParticles);
            view.OnVaultCardClicked(card, feature, () => opened++);
            Assert.Equal(1, opened);
            Assert.True(canvas.BurstCount > 0);
            for (int i = 0; i < 50 && opened == 1; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.Post(() => { });
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(2, opened);
        }
        finally
        {
            shell.Close();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
    });
    [Fact]
    public Task PointerClickPressesAndOpensAfterTheWpfDelay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard" &&
                c.DataContext is ExclusiveCardRow row && row.Feature.Key == "remotecontrol");
            card.BringIntoView();
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            TimeSpan? delay = null;
            Action? complete = null;
            view.VaultClickDelay = (d, action) => { delay = d; complete = action; };
            var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(point, MouseButton.Left);
            shell.MouseUp(point, MouseButton.Left);

            Assert.Equal(TimeSpan.FromMilliseconds(140), delay);
            Assert.NotNull(complete);
            Assert.Equal("premium", shell.CurrentTab);
            var scale = Assert.IsType<ScaleTransform>(card.RenderTransform);
            Assert.Equal(0.97, scale.GetBaseValue(ScaleTransform.ScaleXProperty).Value, 3);
            Assert.Equal(0.97, scale.GetBaseValue(ScaleTransform.ScaleYProperty).Value, 3);
            Assert.Equal(2, scale.Transitions!.Count);
            Assert.All(scale.Transitions, t => Assert.Equal(TimeSpan.FromMilliseconds(80), ((DoubleTransition)t).Duration));

            complete!();
            Assert.Equal(1, scale.GetBaseValue(ScaleTransform.ScaleXProperty).Value, 3);
            Assert.Equal(1, scale.GetBaseValue(ScaleTransform.ScaleYProperty).Value, 3);
            Assert.Equal("remotecontrol", shell.CurrentTab);
        }
        finally
        {
            shell.Close();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnavailableCardClickDoesNotBurstOrScheduleAnOpen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode);
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard" &&
                c.DataContext is ExclusiveCardRow row && row.Feature.Key == "backroom");
            card.BringIntoView();
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var canvas = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            Assert.True(AmbientFxCanvas.Env.AllowParticles);
            Assert.Equal(0, canvas.BurstCount);
            bool scheduled = false;
            view.VaultClickDelay = (_, _) => scheduled = true;
            var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(point, MouseButton.Left);
            shell.MouseUp(point, MouseButton.Left);
            Assert.False(scheduled);
            Assert.Equal(0, canvas.BurstCount);
            Assert.Equal("premium", shell.CurrentTab);
        }
        finally
        {
            shell.Close();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>Review 2026-11-06: the Play wall launches Breakout and DtRH on this head, so their Premium cards
    /// open the same doors (WPF OpenExclusiveFeature :205/:208) instead of an inert "not on this build" card.</summary>
    [Theory]
    [InlineData("breakout", true)]
    [InlineData("dtrh", true)]
    [InlineData("focusgaze", true)]
    [InlineData("backroom", false)]
    [InlineData("arcademy", false)]
    public void PremiumCardsMatchThePlayWallDoors(string key, bool onThisBuild) =>
        Assert.Equal(onThisBuild, ExclusivesTabView.IsOnThisBuild(key));
}
