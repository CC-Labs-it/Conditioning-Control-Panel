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
    public void DimWhenInactiveRestsOffTileDimAndGreyAndHoverHandsItBack() => WithCard((window, host, card) =>
    {
        var content = card.FindControl<Grid>("ContentRoot")!;
        var mute = card.FindControl<Border>("ImgIconMute")!;
        var title = card.FindControl<TextBlock>("TxtTitle")!;
        var art = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = art.Lock())
            System.Runtime.InteropServices.Marshal.Copy(Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 4).SelectMany(p => p).ToArray(), 0, fb.Address, 16);
        card.Icon = art;
        var grey = (global::Avalonia.Media.Imaging.Bitmap)((ImageBrush)mute.Background!).Source!;
        var px = new byte[16];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { grey.CopyPixels(new PixelRect(0, 0, 2, 2), pin.AddrOfPinnedObject(), 16, 8); } finally { pin.Free(); }
        Assert.Equal(new byte[] { 76, 76, 76, 255 }, px.Take(4).ToArray()); // WPF luma 0.299 R

        Assert.Equal(1.0, content.Opacity);           // opt-in: a plain card never dims
        card.DimWhenInactive = true;
        Assert.Equal(0.62, content.Opacity, 3);
        Assert.Equal(0.72, title.Opacity, 3);
        Assert.NotNull(mute.Transitions);              // loaded + transitions: the 200 ms fade
        CoreSettings.Current.MotionLevel = MotionLevel.Off;   // Off snaps, so Opacity reads at its target
        Env.RaiseMotionGateChanged();
        Assert.Null(mute.Transitions);
        Assert.Equal(1.0, mute.Opacity);
        Hover(card, window, true);
        Assert.Equal(1.0, content.Opacity);
        Assert.Equal(0.0, mute.Opacity);
        Hover(card, window, false);
        card.IsActive = true;
        Assert.Equal(1.0, content.Opacity);
        Assert.Equal(0.0, mute.Opacity);
        Assert.Equal(1.0, title.Opacity);
        card.IsActive = false;
        card.IsLocked = true;                          // the lock veil is a different sentence
        Assert.Equal(0.35, content.Opacity, 3);
        Assert.Equal(0.0, mute.Opacity);
        card.IsLocked = false;
        card.TeaseTier = 1;                            // a teased tile keeps its blur, no grey
        Assert.Equal(0.0, mute.Opacity);
        card.TeaseTier = 0;
        Assert.Equal(1.0, mute.Opacity);
    });

    [Fact]
    public void DashboardDepthPressesTheFaceHoldsTheDimAndSwapsClicksOnRequest() => WithCard((window, host, card) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;   // snap, so travel reads at its target
        Env.RaiseMotionGateChanged();
        card.DashboardDepth = true;
        card.DimWhenInactive = true;
        host.Children.Remove(card);
        host.Children.Add(card);                       // WPF: the socket shows on Loaded
        Dispatcher.UIThread.RunJobs();
        var content = card.FindControl<Grid>("ContentRoot")!;
        var scale = (ScaleTransform)card.FindControl<Border>("RootBorder")!.RenderTransform!;
        Assert.True(card.FindControl<Border>("DepthSocket")!.IsVisible);
        Assert.True(card.FindControl<Border>("DepthBevel")!.IsVisible);
        Assert.Equal(-DashboardCardDepth.RestLiftPx, card.Depth.Travel, 3);

        Hover(card, window, true);
        Assert.Equal(0.62, content.Opacity, 3);        // the dashboard keeps an off tile dim
        Assert.Equal(1.0, card.FindControl<Border>("ImgIconMute")!.Opacity);
        Assert.Equal(1.0, scale.ScaleX);              // no hover lift: the face is the motion
        Hover(card, window, false);

        int clicks = 0, toggles = 0;
        card.Click += (_, _) => clicks++;
        card.ToggleRequested += (_, _) => toggles++;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        var at = new Point(90, 70);
        window.MouseDown(at, MouseButton.Left);
        Assert.True(card.Depth.IsPressed);
        Assert.Equal(2, card.Depth.Travel, 3);         // DepthLaw.PressTravelPx
        window.MouseUp(at, MouseButton.Left);
        Assert.False(card.Depth.IsPressed);
        Assert.Equal((1, 0), (clicks, toggles));
        CoreSettings.Current.DashboardInvertClicks = true;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Assert.Equal((2, 1), (clicks, toggles));       // swapped: left toggles, right opens
        card.IsLocked = true;
        window.MouseDown(at, MouseButton.Left);
        Assert.False(card.Depth.IsPressed);            // a locked tile does not press
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal((2, 1), (clicks, toggles));       // and cannot be toggled
        card.IsLocked = false;
        card.IsActive = true;
        Assert.Equal(1, card.Depth.Travel, 3);         // DepthLaw.ActiveSinkPx: on sits in the socket
        Assert.Equal(0.75, card.FindControl<Border>("DepthBevel")!.Opacity, 3);

        Assert.False(card.FindControl<Border>("V2BadgeHost")!.IsVisible);
        card.ShowV2Badge = true;
        Assert.True(card.FindControl<Border>("V2BadgeHost")!.IsVisible);
    });

    [Fact]
    public void ReleaseSpringOvershootsRestThenSettles()
    {
        var spring = new DashboardCardDepth.ReleaseSpring(0.5);
        Assert.Equal(1.5, spring.Ease(0.65), 3);
        Assert.Equal(1.0, spring.Ease(1.0), 3);
        Assert.Equal(0.0, spring.Ease(0.0), 3);
    }

    [Fact]
    public void ArtDesaturateGreysALargeDecodedPngOnTheScalingPath() => WithCard((_, _, _) =>
    {
        // A real 1024 px PNG (decoded, not a WriteableBitmap) takes the CreateScaledBitmap path.
        using var src = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(1024, 1024), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = src.Lock())
        {
            var red = new byte[fb.RowBytes * 1024];
            for (int i = 0; i < red.Length; i += 4) { red[i] = 0; red[i + 1] = 0; red[i + 2] = 255; red[i + 3] = 255; }
            System.Runtime.InteropServices.Marshal.Copy(red, 0, fb.Address, red.Length);
        }
        var png = new System.IO.MemoryStream();
        src.Save(png);
        png.Position = 0;
        using var decoded = new global::Avalonia.Media.Imaging.Bitmap(png);

        var grey = ArtDesaturate.Of(decoded);
        Assert.NotNull(grey);
        Assert.Equal(new PixelSize(ArtDesaturate.MaxEdge, ArtDesaturate.MaxEdge), grey!.PixelSize);
        var px = new byte[4];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { grey.CopyPixels(new PixelRect(160, 160, 1, 1), pin.AddrOfPinnedObject(), 4, 4); }
        finally { pin.Free(); }
        // Pure red -> 0.299 x 255 = 76 in every channel, whatever order the scaled copy uses.
        Assert.Equal(new byte[] { 76, 76, 76, 255 }, px);
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
        CoreSettings.Current.DashboardInvertClicks = false;
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
