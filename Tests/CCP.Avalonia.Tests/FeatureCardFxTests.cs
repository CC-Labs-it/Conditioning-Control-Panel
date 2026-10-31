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
    public void QuickReleaseRetargetsFromAnimatedPressAndOvershootsOnePixel() => WithCard((window, host, card) =>
    {
        card.DashboardDepth = true;
        var travel = ((TransformGroup)card.FindControl<Panel>("PressFace")!.RenderTransform!)
            .Children.OfType<TranslateTransform>().Single();
        var clock = new SharedDeeperThemeTests.StepClock();
        travel.SetValue(SharedDeeperThemeTests.StepClock.Property, clock.Instance);
        window.UpdateLayout();
        var at = card.TranslatePoint(new Point(80, 100), window)!.Value;
        window.MouseMove(at);
        clock.Step(0); clock.Step(140);
        Assert.Equal(-2, travel.Y, 6);
        window.MouseDown(at, MouseButton.Left);
        var transition = Assert.Single(travel.Transitions!);
        clock.Step(1000); clock.Step(1030); // 30 of the 90 ms press, still travelling down
        double from = travel.Y;
        Assert.Equal(2.0 / 9, from, 6);
        window.MouseUp(at, MouseButton.Left);
        clock.Step(2000);
        Assert.Equal(from, travel.Y, 6); // no jump back to the press target
        Assert.Same(transition, Assert.Single(travel.Transitions!));
        clock.Step(2091); // 65 percent of the 140 ms release: WPF target minus exactly 1 px
        Assert.Equal(-3, travel.Y, 6);
        clock.Step(2140);
        Assert.Equal(-2, travel.Y, 6);
    });

    [Fact]
    public void PointerMovesRetargetOneTiltTransitionAndIgnoreRepeatedTargets() => WithCard((window, host, card) =>
    {
        card.DashboardDepth = true;
        window.UpdateLayout();
        var face = card.FindControl<Panel>("PressFace")!;
        var tilt = ((TransformGroup)face.RenderTransform!).Children.OfType<RotateTransform>().Single();
        void Move(double x) => window.MouseMove(face.TranslatePoint(new Point(x, 30), window)!.Value);
        Move(20);
        var transition = Assert.Single(tilt.Transitions!);
        var target = tilt.GetBaseValue(RotateTransform.AngleProperty).Value;
        int replacements = 0, writes = 0;
        tilt.PropertyChanged += (_, e) =>
        {
            if (e.Property == global::Avalonia.Animation.Animatable.TransitionsProperty) replacements++;
            if (e.Property == RotateTransform.AngleProperty && e.Priority == global::Avalonia.Data.BindingPriority.LocalValue) writes++;
        };
        Move(20);
        // Native pointer coordinates round to pixels on the moving face. Repeat the exact
        // target separately, while real pointer moves below check transition continuity.
        writes = 0;
        var repeated = tilt.GetBaseValue(RotateTransform.AngleProperty).Value;
        DashboardCardDepth.Move(tilt, RotateTransform.AngleProperty, repeated,
            ConditioningControlPanel.Services.UI.DepthLaw.HoverMs, new global::Avalonia.Animation.Easings.LinearEasing());
        Assert.Equal(0, writes);
        Move(40); Move(60);
        Assert.NotEqual(target, tilt.GetBaseValue(RotateTransform.AngleProperty).Value);
        Assert.Same(transition, Assert.Single(tilt.Transitions!));
        Assert.Equal(0, replacements);
    });

    [Fact]
    public void DashboardFaceTravelsAndParksThroughLiveMotionGates() => WithCard((window, host, card) =>
    {
        card.DashboardDepth = card.DimWhenInactive = true;
        card.RefreshFx();
        var face = card.FindControl<Panel>("PressFace")!;
        var travel = ((TransformGroup)face.RenderTransform!).Children.OfType<TranslateTransform>().Single();
        var tilt = ((TransformGroup)face.RenderTransform!).Children.OfType<RotateTransform>().Single();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Equal(-1, travel.Y);
        Assert.Null(travel.Transitions);
        Assert.True(card.FindControl<Border>("DepthSocket")!.IsVisible);
        Assert.Equal(0, new DashboardCardDepth.ReleaseSpring(0.5).Ease(0));
        Assert.Equal(1.5, new DashboardCardDepth.ReleaseSpring(0.5).Ease(0.65));
        Assert.Equal(1, new DashboardCardDepth.ReleaseSpring(0.5).Ease(1));
        window.UpdateLayout();
        var at = card.TranslatePoint(new Point(80, 100), window)!.Value;
        window.MouseMove(at);
        Assert.Equal(-2, travel.Y);
        window.MouseDown(at, MouseButton.Left);
        Assert.True(card.Depth.IsPressed);
        Assert.Equal(2, travel.Y);
        window.MouseUp(at, MouseButton.Left);
        Assert.False(card.Depth.IsPressed);
        card.IsActive = true;
        Assert.Equal(1, travel.Y);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Env.RaiseMotionGateChanged();
        card.IsActive = false;
        window.MouseMove(at + new Vector(15, 15));
        Assert.NotEqual(0, tilt.GetBaseValue(RotateTransform.AngleProperty).Value);
        CoreSettings.Current.PerformanceMode = true;
        Env.RaiseMotionGateChanged();
        Assert.Equal(0, tilt.GetBaseValue(RotateTransform.AngleProperty).Value);
        window.MouseDown(at, MouseButton.Left);
        host.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.Depth.IsPressed);
        Assert.Equal(-1, travel.Y);
        Assert.Null(travel.Transitions);
        host.IsVisible = true;
        card.IsLocked = true;
        window.MouseDown(at, MouseButton.Left);
        Assert.False(card.Depth.IsPressed);
    });

    [Fact]
    public void InactiveArtUsesSharedGreyCacheAndLockTeasePreviewPriorities() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        using var art = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(640, 320), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        card.Icon = art;
        card.DimWhenInactive = true;
        var mute = card.FindControl<Border>("ImgIconMute")!;
        var content = card.FindControl<Grid>("ContentRoot")!;
        var title = card.FindControl<TextBlock>("TxtTitle")!;
        Assert.Equal(1, mute.Opacity);
        Assert.Equal(0.62, content.Opacity);
        Assert.Equal(0.72, title.Opacity);
        var grey = (global::Avalonia.Media.Imaging.Bitmap)((ImageBrush)mute.Background!).Source!;
        Assert.Equal(new PixelSize(320, 160), grey.PixelSize);
        card.Icon = null;
        card.Icon = art;
        Assert.Same(grey, ((ImageBrush)mute.Background!).Source);
        Hover(card, window, true);
        Assert.Equal(0, mute.Opacity);
        Assert.Equal(1, content.Opacity);
        Hover(card, window, false);
        card.DashboardDepth = true;
        Hover(card, window, true);
        Assert.Equal(1, mute.Opacity);
        Assert.Equal(0.62, content.Opacity);
        Hover(card, window, false);
        card.SetTutorialPreview(true);
        Assert.Equal(-DashboardCardDepth.RestLiftPx, card.Depth.Travel);
        Assert.Equal(0.9, card.FindControl<Border>("DepthBevel")!.Opacity);
        Assert.Equal(0, mute.Opacity);
        Assert.Equal(1, content.Opacity);
        card.SetTutorialPreview(false);
        Assert.Equal(-DashboardCardDepth.RestLiftPx, card.Depth.Travel);
        card.TeaseTier = 1;
        Assert.Equal(0, mute.Opacity);
        card.TeaseTier = 0;
        card.IsLocked = true;
        Assert.Equal(0, mute.Opacity);
        Assert.Equal(0.35, content.Opacity);
        card.IsLocked = false;
        card.IsActive = true;
        Assert.Equal(0, mute.Opacity);
        card.IsActive = false;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Env.RaiseMotionGateChanged();
        Assert.Single(mute.Transitions!);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Null(mute.Transitions);
    });

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
