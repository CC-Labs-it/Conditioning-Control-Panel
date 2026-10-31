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

    [Fact]
    public void InactiveArtUsesGreyTwinAndRestoresForLockTeasePreviewAndHover() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        using var art = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(640, 320), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        card.Icon = art;
        card.DimWhenInactive = true;
        var grey = card.FindControl<Border>("ImgIconMute")!;
        var content = card.FindControl<Grid>("ContentRoot")!;
        var title = card.FindControl<TextBlock>("TxtTitle")!;
        Assert.Equal(new PixelSize(320, 160), ((global::Avalonia.Media.Imaging.Bitmap)((ImageBrush)grey.Background!).Source!).PixelSize);
        Assert.Same(grey.Parent, card.FindControl<Border>("ImgIconHost"));
        Assert.Equal(1, grey.Opacity);
        Assert.Equal(0.62, content.Opacity);
        Assert.Equal(0.72, title.Opacity);
        Hover(card, window, true);
        Assert.Equal(0, grey.Opacity);
        Assert.Equal(1, content.Opacity);
        card.DashboardDepth = true;
        Assert.Equal(1, grey.Opacity); // dashboard hover keeps off art muted
        card.IsActive = true;
        Assert.Equal(0, grey.Opacity);
        Assert.Equal(1, content.Opacity);
        card.IsActive = false;
        card.IsLocked = true;
        Assert.Equal(0, grey.Opacity);
        Assert.Equal(0.35, content.Opacity);
        card.IsLocked = false;
        card.TeaseTier = 1;
        Assert.Equal(0, grey.Opacity);
        card.TeaseTier = 0;
        Assert.Equal(1, grey.Opacity);
        card.SetTutorialPreview(true);
        Assert.Equal(0, grey.Opacity);
        card.SetTutorialPreview(false);
        Assert.Equal(1, grey.Opacity);
        card.Icon = null;
        Assert.Null(grey.Background);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Env.RaiseMotionGateChanged();
        Assert.Equal(TimeSpan.FromMilliseconds(200), ((global::Avalonia.Animation.DoubleTransition)grey.Transitions!.Single()).Duration);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Null(grey.Transitions);
    });

    [Fact]
    public void DepthFaceTravelsAndReleasesWhileSocketStaysFixed() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        card.DashboardDepth = card.DimWhenInactive = true;
        var face = card.FindControl<Panel>("PressFace")!;
        var transforms = (TransformGroup)face.RenderTransform!;
        var travel = transforms.Children.OfType<TranslateTransform>().Single();
        var socket = card.FindControl<Border>("DepthSocket")!;
        var bevel = card.FindControl<Border>("DepthBevel")!;
        Assert.True(socket.IsVisible && bevel.IsVisible);
        Assert.Null(socket.RenderTransform);
        Assert.Equal(-1, travel.Y);
        Hover(card, window, true);
        Assert.Equal(-2, travel.Y);
        Assert.Equal(1, ((ScaleTransform)card.FindControl<Border>("RootBorder")!.RenderTransform!).ScaleX);
        var point = card.TranslatePoint(new Point(60, 60), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        Assert.Equal(2, travel.Y);
        Assert.Equal(1, bevel.Opacity);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(-2, travel.Y);
        card.IsActive = true;
        Assert.Equal(1, travel.Y);
        Assert.Equal(0.75, bevel.Opacity);
        card.IsLocked = true;
        Assert.Equal(-2, travel.Y);
        window.MouseDown(point, MouseButton.Left);
        Assert.Equal(-2, travel.Y);
        window.MouseUp(point, MouseButton.Left);
        host.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(-1, travel.Y);
        card.DashboardDepth = false;
        Assert.Equal(0, travel.Y);
        Assert.False(socket.IsVisible || bevel.IsVisible);
    });

    [Fact]
    public void DepthMotionUsesReleaseSpringPointerTiltAndLiveGates() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        card.DashboardDepth = card.DimWhenInactive = card.IsActive = true;
        var transforms = (TransformGroup)card.FindControl<Panel>("PressFace")!.RenderTransform!;
        var travel = transforms.Children.OfType<TranslateTransform>().Single();
        var tilt = transforms.Children.OfType<RotateTransform>().Single();
        Assert.Equal(1, travel.Y);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Env.RaiseMotionGateChanged();
        card.IsActive = false;
        var spring = (global::Avalonia.Animation.DoubleTransition)travel.Transitions!.Single();
        Assert.Equal(TimeSpan.FromMilliseconds(140), spring.Duration);
        Assert.Equal(1.5, spring.Easing.Ease(0.65), 3); // WPF: 1 px beyond the -1 rest, from +1
        window.MouseMove(card.TranslatePoint(new Point(card.Bounds.Width * 0.75, card.Bounds.Height * 0.25), window)!.Value);
        Assert.Equal(0.3, tilt.GetBaseValue(RotateTransform.AngleProperty).Value, 3);
        Assert.Equal(TimeSpan.FromMilliseconds(120), ((global::Avalonia.Animation.DoubleTransition)tilt.Transitions!.Single()).Duration);
        CoreSettings.Current.PerformanceMode = true;
        Env.RaiseMotionGateChanged();
        Assert.Equal(0, tilt.GetBaseValue(RotateTransform.AngleProperty).Value);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Equal(-2, travel.Y); // hovered off face snaps
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        Env.RaiseMotionGateChanged();
        card.IsActive = true;
        Assert.Equal(TimeSpan.FromMilliseconds(70), ((global::Avalonia.Animation.DoubleTransition)travel.Transitions!.Single()).Duration);
    });

    [Theory]
    [InlineData(140, 120)]
    [InlineData(212, 190)]
    [InlineData(424, 190)]
    [InlineData(424, 380)]
    public void BadgeColumnStacksMarksAwayFromHelp(double width, double height) => WithCard((window, host, card) =>
    {
        card.Width = width; card.Height = height;
        card.ShowV2Badge = true;
        card.TierBadge = "TIER 1";
        card.HelpSectionId = "BubblePop";
        window.UpdateLayout();
        var tier = card.FindControl<Border>("TierBadgeHost")!;
        var pill = card.FindControl<Border>("V2BadgeHost")!;
        var help = card.FindControl<Button>("BtnHelp")!;
        Rect Box(Control c) => new(c.TranslatePoint(default, card)!.Value, c.Bounds.Size);
        Assert.True(pill.IsVisible && help.IsVisible);
        Assert.True(Box(pill).Top >= Box(tier).Bottom + 4);
        Assert.Equal(Box(tier).Left, Box(pill).Left);
        Assert.False(Box(pill).Intersects(Box(help)));
        double top = Box(tier).Top;
        card.TierBadge = null;
        window.UpdateLayout();
        Assert.Equal(top, Box(pill).Top);
        Assert.False(card.FindControl<StackPanel>("BadgeColumn")!.IsHitTestVisible);
        card.ShowV2Badge = false;
        Assert.False(pill.IsVisible);
    });

    [Fact]
    public void WallBadgesFollowOwnershipChangesAndReattach() => WithCard((window, host, card) =>
    {
        var field = typeof(ConditioningControlPanel.Services.Prizes.PrizeGrants)
            .GetField("_service", BindingFlags.Static | BindingFlags.NonPublic)!;
        var saved = (ConditioningControlPanel.Services.Prizes.OwnershipService?)field.GetValue(null);
        var ownership = new ConditioningControlPanel.Services.Prizes.OwnershipService(() => "test-card", null, a => a());
        ConditioningControlPanel.Services.Prizes.PrizeGrants.Attach(ownership);
        var wall = new ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView();
        try
        {
            host.Children.Add(wall);
            Dispatcher.UIThread.RunJobs();
            var flash = wall.FindControl<FeatureCard>("CardFlash")!;
            var bubble = wall.FindControl<FeatureCard>("CardBubblePop")!;
            Assert.False(flash.ShowV2Badge || bubble.ShowV2Badge);
            ownership.ApplySnapshot("test-card", 1, new[] { ConditioningControlPanel.Services.Prizes.PrizeGrants.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.ShowV2Badge);
            Assert.False(flash.ShowV2Badge);
            ownership.ApplySnapshot("test-card", 2, new[] { ConditioningControlPanel.Services.Prizes.PrizeGrants.FlashPendulum });
            Dispatcher.UIThread.RunJobs();
            Assert.True(flash.ShowV2Badge);
            Assert.False(bubble.ShowV2Badge);
            host.Children.Remove(wall);
            Dispatcher.UIThread.RunJobs();
            ownership.ApplySnapshot("test-card", 3, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.True(flash.ShowV2Badge); // detached view unsubscribed
            host.Children.Add(wall);
            Dispatcher.UIThread.RunJobs();
            Assert.False(flash.ShowV2Badge || bubble.ShowV2Badge);
        }
        finally { host.Children.Remove(wall); ConditioningControlPanel.Services.Prizes.PrizeGrants.Attach(saved); }
    });

    [Fact]
    public void MuteReversalsContinueFromCurrentOpacityOnTheAnimationClock() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        card.DimWhenInactive = true;
        var grey = card.FindControl<Border>("ImgIconMute")!;
        var clock = new AnimationClock(grey);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Hover(card, window, true);
        clock.At(0); clock.At(50);
        Assert.Equal(0.5625, grey.Opacity, 5); // 50 ms into the WPF 200 ms quadratic fade
        double before = grey.Opacity;
        Hover(card, window, false);
        Assert.Equal(before, grey.Opacity, 5);
        clock.At(50); clock.At(100);
        Assert.InRange(grey.Opacity, before + 0.01, 0.99);
        before = grey.Opacity;
        card.IsActive = true;
        Assert.Equal(before, grey.Opacity, 5);
        clock.At(100); clock.At(150);
        Assert.InRange(grey.Opacity, 0.01, before - 0.01);
        before = grey.Opacity;
        card.RefreshFx();
        Assert.Equal(before, grey.Opacity, 5);
        clock.At(350);
        Assert.Equal(0, grey.Opacity, 5);
    });

    [Fact]
    public void FocusRefreshMidReleasePreservesTheSpringAndItsOriginalDeadline() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        card.DashboardDepth = card.DimWhenInactive = true;
        var travel = ((TransformGroup)card.FindControl<Panel>("PressFace")!.RenderTransform!).Children.OfType<TranslateTransform>().Single();
        var clock = new AnimationClock(travel);
        var point = card.TranslatePoint(new Point(60, 60), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Assert.Equal(2, travel.Y);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        window.MouseUp(point, MouseButton.Left);
        clock.At(0); clock.At(35);
        Assert.InRange(travel.Y, -1.9, 1.9);
        double before = travel.Y;
        var transition = travel.Transitions;
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, travel.Y, 5);
        Assert.Same(transition, travel.Transitions);
        clock.At(91); // 65% of 140 ms: one pixel past the hovered -2 rest
        Assert.Equal(-3, travel.Y, 5);
        clock.At(140);
        Assert.Equal(-2, travel.Y, 5);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        Env.RaiseMotionGateChanged();
        Assert.Equal(-2, travel.Y);
    });

    // Avalonia's animation clock is internal. Drive its real ClockBase with exact timestamps
    // so these are timed transition tests without sleeps or dependence on renderer scheduling.
    private sealed class AnimationClock
    {
        private readonly object _clock = Activator.CreateInstance(
            typeof(global::Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase")!, nonPublic: true)!;
        internal AnimationClock(global::Avalonia.Animation.Animatable target) =>
            typeof(global::Avalonia.Animation.Animatable).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, _clock);
        internal void At(int milliseconds) => _clock.GetType().GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_clock, new object[] { TimeSpan.FromMilliseconds(milliseconds) });
    }

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
