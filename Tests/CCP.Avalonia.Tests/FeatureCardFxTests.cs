using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace CCP.Avalonia.Tests;

public sealed class FeatureCardFxTests
{
    [Fact]
    public void TeaseRimUsesExistingSharedClockAndParksWithoutLosingLivery() => WithCard((window, host, card) =>
    {
        var root = card.FindControl<Border>("RootBorder")!;
        card.TeaseTier = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, TierFxBorder.GetTier(root));
        Assert.Equal(2, TierFxBorder.GetRimThickness(root));
        Assert.Equal(new Thickness(2), root.BorderThickness);
        var gold = window.GetVisualDescendants().OfType<TierFxBorderAdorner>().Single();
        Assert.True(gold.IsAnimating);
        var clock = (FrameClock)typeof(TierFxBorderAdorner)
            .GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(gold)!;
        Assert.True(clock.IsFrameLocked);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), clock.Interval);
        Assert.Null(root.Effect);
        Assert.NotNull(card.FindControl<Border>("ImgIconHost")!.Effect); // static art veil only

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Env.RaiseMotionGateChanged();
        Assert.False(gold.IsAnimating);
        Assert.Equal(new Thickness(2), root.BorderThickness);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Env.RaiseMotionGateChanged();
        Assert.True(gold.IsAnimating);
        CoreSettings.Current.PerformanceMode = true;
        Env.RaiseMotionGateChanged();
        Assert.False(gold.IsAnimating);
        CoreSettings.Current.PerformanceMode = false;
        Env.RaiseMotionGateChanged();
        Assert.True(gold.IsAnimating);

        host.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(gold.IsAnimating);
        Assert.False(gold.IsVisible);
        host.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(gold.IsAnimating);
        card.TeaseTier = 2;
        Dispatcher.UIThread.RunJobs();
        var diamond = window.GetVisualDescendants().OfType<TierFxBorderAdorner>().Single();
        Assert.NotSame(gold, diamond);
        Assert.False(gold.IsAnimating);
        Assert.Null(gold.Parent);
        Assert.True(diamond.IsAnimating);

        card.TeaseTier = 0;
        Assert.Equal(0, TierFxBorder.GetTier(root));
        Assert.Equal(new Thickness(1), root.BorderThickness);
        Assert.Null(card.FindControl<Border>("ImgIconHost")!.Effect);
        Assert.False(diamond.IsAnimating);
        Assert.Null(diamond.Parent);
        card.TeaseTier = 1;
        Dispatcher.UIThread.RunJobs();
        var reattached = window.GetVisualDescendants().OfType<TierFxBorderAdorner>().Single();
        host.Children.Remove(card);
        Dispatcher.UIThread.RunJobs();
        Assert.False(reattached.IsAnimating);
        Assert.Null(reattached.Parent);
    });

    [Fact]
    public void HoverSnapsAtOffAndRestoresShortTransitionsAtReduced() => WithCard((window, host, card) =>
    {
        var root = card.FindControl<Border>("RootBorder")!;
        var scale = (ScaleTransform)root.RenderTransform!;
        var rim = card.FindControl<Border>("RimLight")!;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Hover(card, window, true);
        Assert.Null(scale.Transitions);
        Assert.Null(rim.Transitions);
        Assert.Equal(1.02, scale.ScaleX, 3);
        Assert.Equal(0.85, rim.Opacity, 3);
        Hover(card, window, false);
        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(0, rim.Opacity);

        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        CoreSettings.Current.PerformanceMode = true;
        Env.RaiseMotionGateChanged();
        Assert.Equal(2, scale.Transitions!.Count);
        Assert.Single(rim.Transitions!); // short interaction motion survives Reduced and Performance
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        card.IsLocked = true;
        Hover(card, window, true);
        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(0, rim.Opacity);
    });

    [Fact]
    public void ActiveBreathRechecksHostAndLiveGatesAndRetainsVisibilityPark() => WithCard((window, host, card) =>
    {
        var glow = card.FindControl<Border>("GlowLayer")!;
        var ring = card.FindControl<Border>("ActiveBorder")!;
        card.IsActive = true;
        Assert.True(card.IsBreathing);
        Assert.True(glow.BoxShadow.Count > 0);
        Assert.Null(card.FindControl<Border>("RootBorder")!.Effect);
        window.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsBreathing);
        Assert.Equal(0.9, glow.Opacity, 3);
        window.WindowState = WindowState.Normal;
        window.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(card.IsBreathing);
        // Headless keeps multiple windows active; enter the real platform notification handler
        // (as PanicKeyTests does), so IsActive and Deactivated change together.
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null);
        Assert.False(window.IsActive);
        Assert.False(card.IsBreathing);
        Assert.Equal(0.9, glow.Opacity, 3);
        typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsActive);
        Assert.True(card.IsBreathing);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Env.RaiseMotionGateChanged();
        Assert.False(card.IsBreathing);
        Assert.Equal(0.9, glow.Opacity, 3);
        Assert.Equal(1, ring.Opacity);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Equal(0, glow.Opacity);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = true;
        Env.RaiseMotionGateChanged();
        Assert.False(card.IsBreathing);
        Assert.Equal(0, glow.Opacity);
        CoreSettings.Current.PerformanceMode = false;
        Env.RaiseMotionGateChanged();
        Assert.True(card.IsBreathing);
        host.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsBreathing);
        host.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(card.IsBreathing);
        card.IsLocked = true;
        Assert.False(card.IsBreathing);
        Assert.False(ring.IsVisible);
        Assert.Equal(0, glow.Opacity);
        card.IsLocked = false;
        host.Children.Remove(card);
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.IsBreathing);
        Env.RaiseMotionGateChanged();
        Assert.False(card.IsBreathing);
    });

    private static void Hover(FeatureCard card, Window window, bool on) => card.RaiseEvent(new PointerEventArgs(
        on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, card,
        new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));

    private static void WithCard(Action<Window, StackPanel, FeatureCard> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var card = new FeatureCard { Title = "Feature", Width = 180, Height = 140 };
        var host = new StackPanel { Children = { card } };
        var window = new Window { Content = host, Width = 240, Height = 220 };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            check(window, host, card);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
        }
    });
}
