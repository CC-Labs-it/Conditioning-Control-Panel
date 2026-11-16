using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SuperCornerTests
{
    [Theory]
    [InlineData("FlashImages", SuperEffect.FlickerDeck)]
    [InlineData("Video", SuperEffect.LightsDown)]
    [InlineData("Subliminals", SuperEffect.Afterglow)]
    [InlineData("BouncingText", SuperEffect.Scrawl)]
    [InlineData("SpiralOverlay", SuperEffect.Vortex)]
    [InlineData("PinkFilter", SuperEffect.Creep)]
    [InlineData("BrainDrain", SuperEffect.Undertow)]
    [InlineData("BubblePop", SuperEffect.InnerBloom)]
    public void HelpMapsToEffect(string help, SuperEffect effect) => Assert.Equal(effect, SuperDoor.ForHelp(help));

    [Fact]
    public void UnknownHelpHasNoEffect()
    {
        Assert.Null(SuperDoor.ForHelp(null));
        Assert.Null(SuperDoor.ForHelp("LockCard"));
    }

    [Fact]
    public void TwistUsesTheEffectDescription()
    {
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("super_twist_scrawl"), SuperDoor.Twist(SuperEffect.Scrawl));
        Assert.NotEqual(SuperDoor.Twist(SuperEffect.Creep), SuperDoor.Twist(SuperEffect.Scrawl));
    }

    [Fact]
    public void PressRefusesFreeAndFlipsPaid() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var premium = CoreEntitlement.HasPremiumProvider;
        var denied = CoreEntitlement.ShowDeniedHandler;
        var trying = SuperAccess.TryingProvider;
        var overrideValue = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var service = new SettingsService();
        int refusals = 0, changes = 0;
        void Changed(SuperEffect e) { if (e == SuperEffect.Scrawl) changes++; }
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            SuperAccess.TryingProvider = () => null;
            SuperAccess.Changed += Changed;
            Assert.False(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.Equal(1, refusals);
            Assert.Empty(service.Current.SuperModes);
            Assert.Equal(0, changes);
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.True(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.True(SuperDoor.Press(SuperEffect.Scrawl));
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.Equal(2, changes);
        }
        finally
        {
            SuperAccess.Changed -= Changed;
            SuperAccess.TryingProvider = trying;
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.ShowDeniedHandler = denied;
            CoreSettings.ServiceProvider = provider;
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", overrideValue);
            service.SealForReset();
        }
    });

    [Fact]
    public void SingleTileRoutesTheActualCornerAndKeepsBadgesClear() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "BouncingText", DashboardDepth = true, DimWhenInactive = true },
        (window, service, tile) =>
        {
            var card = (FeatureCard)tile;
            var corner = Assert.Single(card.GetVisualDescendants().OfType<SuperCorner>());
            Assert.Equal(SuperEffect.Scrawl, corner.Effect);
            Assert.Equal(SuperCorner.Leg + 2, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);
            Assert.True(corner.HitTest(new Point(4, 4)));
            Assert.False(corner.HitTest(new Point(26, 26)));
            int clicks = 0, toggles = 0, refusals = 0;
            card.Click += (_, _) => clicks++;
            card.ToggleRequested += (_, _) => toggles++;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            CoreEntitlement.HasPremiumProvider = () => false;
            Click(window, corner, new Point(8, 8), MouseButton.Right);
            Assert.Equal(1, refusals);
            Assert.Empty(service.Current.SuperModes);
            Assert.Equal(0, toggles);
            Click(window, corner, new Point(8, 8), MouseButton.Left);
            Assert.Equal(1, clicks);
            CoreEntitlement.HasPremiumProvider = () => true;
            SuperAccess.RaiseChanged(SuperEffect.Scrawl);
            Dispatcher.UIThread.RunJobs();
            Assert.True(corner.Lit);
            Click(window, corner, new Point(8, 8), MouseButton.Right);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.False(corner.Lit);
            Assert.Equal(0, toggles);
            Click(window, corner, new Point(26, 26), MouseButton.Right);
            Assert.Equal(1, toggles); // outside the triangle remains the base switch
            service.Current.DashboardInvertClicks = true;
            Assert.True(corner.Inverted());
            window.MouseMove(corner.TranslatePoint(new Point(8, 8), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            var tip = Assert.IsType<StackPanel>(Assert.IsType<ToolTip>(ToolTip.GetTip(corner)).Content);
            Assert.Equal(SuperDoor.Twist(SuperEffect.Scrawl), ((TextBlock)tip.Children[1]).Text);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("super_corner_tip_inverted"), ((TextBlock)tip.Children[2]).Text);
            Click(window, corner, new Point(8, 8), MouseButton.Left);
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.True(corner.Lit);
            Assert.Equal(1, clicks);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            SuperAccess.Set(SuperEffect.Scrawl, false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(corner.Lit); // detached corners no longer listen
            window.Content = card;
            Dispatcher.UIThread.RunJobs();
            Assert.False(corner.Lit); // loading reads the current state
            card.HelpSectionId = "LockCard";
            Assert.Empty(card.GetVisualDescendants().OfType<SuperCorner>());
            Assert.Equal(8, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);
        });

    [Fact]
    public void SplitCornersRouteToTheirOwnHalfAndFollowAccessChanges() => WithTile(
        () => new SplitFeatureCard { Width = 180, Height = 180, HelpSectionIdA = "Video", HelpSectionIdB = "PinkFilter", DashboardDepth = true },
        (window, service, tile) =>
        {
            var card = (SplitFeatureCard)tile;
            var corners = card.GetVisualDescendants().OfType<SuperCorner>().ToArray();
            Assert.Equal(2, corners.Length);
            var a = corners.Single(c => !c.BottomRight);
            var b = corners.Single(c => c.BottomRight);
            Assert.Equal(SuperEffect.LightsDown, a.Effect);
            Assert.Equal(SuperEffect.Creep, b.Effect);
            Assert.True(b.HitTest(new Point(26, 26)));
            Assert.False(b.HitTest(new Point(4, 4)));
            Assert.Equal(32, card.FindControl<Border>("TitlePillA")!.Margin.Left);
            Assert.Equal(32, card.FindControl<Border>("TitlePillB")!.Margin.Right);
            int clickA = 0, clickB = 0, toggleA = 0, toggleB = 0;
            card.ClickA += (_, _) => clickA++;
            card.ClickB += (_, _) => clickB++;
            card.ToggleA += (_, _) => toggleA++;
            card.ToggleB += (_, _) => toggleB++;
            Click(window, a, new Point(8, 8), MouseButton.Right);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.LightsDown));
            Assert.True(SuperAccess.IsSwitchedOn(SuperEffect.Creep));
            Click(window, b, new Point(22, 22), MouseButton.Right);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Creep));
            Assert.Equal(0, toggleA + toggleB);
            Click(window, a, new Point(8, 8), MouseButton.Left);
            Click(window, b, new Point(22, 22), MouseButton.Left);
            Assert.Equal(1, clickA);
            Assert.Equal(1, clickB);
            SuperAccess.Set(SuperEffect.Creep, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.Lit);
            card.HelpSectionIdB = "LockCard";
            Assert.Single(card.GetVisualDescendants().OfType<SuperCorner>());
            Assert.Equal(8, card.FindControl<Border>("TitlePillB")!.Margin.Right);
            service.Current.DashboardInvertClicks = true;
            a = card.GetVisualDescendants().OfType<SuperCorner>().Single();
            Assert.True(a.Inverted());
            SuperAccess.Set(SuperEffect.LightsDown, true);
            Dispatcher.UIThread.RunJobs();
            Click(window, a, new Point(8, 8), MouseButton.Left);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.LightsDown));
            Click(window, a, new Point(8, 8), MouseButton.Right);
            Assert.Equal(2, clickA);
        });

    [Fact]
    public void SingleCornerToggleDoesNotToggleBase() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "BouncingText" },
        (window, _, tile) =>
        {
            var card = (FeatureCard)tile;
            int baseToggles = 0;
            card.ToggleRequested += (_, _) => baseToggles++;
            Click(window, card.GetVisualDescendants().OfType<SuperCorner>().Single(), new Point(8, 8), MouseButton.Right);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.Scrawl));
            Assert.Equal(0, baseToggles);
        });

    [Fact]
    public void SplitCornerToggleDoesNotToggleBase() => WithTile(
        () => new SplitFeatureCard { Width = 180, Height = 180, HelpSectionIdA = "Video" },
        (window, _, tile) =>
        {
            var card = (SplitFeatureCard)tile;
            int baseToggles = 0;
            card.ToggleA += (_, _) => baseToggles++;
            Click(window, card.GetVisualDescendants().OfType<SuperCorner>().Single(), new Point(8, 8), MouseButton.Right);
            Assert.False(SuperAccess.IsSwitchedOn(SuperEffect.LightsDown));
            Assert.Equal(0, baseToggles);
        });

    [Fact]
    public void SplitTitlesClearBothCorners() => WithTile(
        () => new SplitFeatureCard { Width = 180, Height = 180, HelpSectionIdA = "Video", HelpSectionIdB = "PinkFilter" },
        (_, _, tile) =>
        {
            var card = (SplitFeatureCard)tile;
            Assert.Equal(32, card.FindControl<Border>("TitlePillA")!.Margin.Left);
            Assert.Equal(32, card.FindControl<Border>("TitlePillB")!.Margin.Right);
            Assert.True(double.IsFinite(card.FindControl<TextBlock>("TxtTitleA")!.MaxWidth));
        });

    [Fact]
    public void TriangleRejectsTheUnusedHalf() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var a = new SuperCorner(SuperEffect.Scrawl, false);
        var b = new SuperCorner(SuperEffect.Scrawl, true);
        Assert.True(a.HitTest(new Point(4, 4)));
        Assert.False(a.HitTest(new Point(26, 26)));
        Assert.True(b.HitTest(new Point(26, 26)));
        Assert.False(b.HitTest(new Point(4, 4)));
    });

    [Fact]
    public void TipShowsTheTwistAndInvertedClickHint() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var corner = new SuperCorner(SuperEffect.Scrawl, false) { Inverted = () => true };
        var tip = (StackPanel)corner.BuildTip();
        Assert.Equal(SuperDoor.Twist(SuperEffect.Scrawl), ((TextBlock)tip.Children[1]).Text);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("super_corner_tip_inverted"), ((TextBlock)tip.Children[2]).Text);
    });

    [Fact]
    public void BadgeInsetTracksTheMappedFeature() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "FlashImages" },
        (_, _, tile) =>
        {
            var card = (FeatureCard)tile;
            Assert.Equal(SuperEffect.FlickerDeck, card.GetVisualDescendants().OfType<SuperCorner>().Single().Effect);
            Assert.Equal(32, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);
            card.HelpSectionId = "LockCard";
            Assert.Empty(card.GetVisualDescendants().OfType<SuperCorner>());
            Assert.Equal(8, card.FindControl<StackPanel>("BadgeColumn")!.Margin.Left);
        });

    [Fact]
    public void AccessEventsRepaintTheAttachedCorner() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "BouncingText" },
        (window, _, tile) =>
        {
            var corner = tile.GetVisualDescendants().OfType<SuperCorner>().Single();
            Assert.True(corner.Lit);
            int on = CornerBrightness(window, corner);
            SuperAccess.Set(SuperEffect.Scrawl, false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(corner.Lit);
            Assert.True(on > CornerBrightness(window, corner) + 40);
        });

    [Fact]
    public void CornerPaintsGoldWhenSelectedAndDarkWhenOff() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "BouncingText" },
        (window, _, tile) =>
        {
            var corner = tile.GetVisualDescendants().OfType<SuperCorner>().Single();
            int gold = CornerBrightness(window, corner);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            SuperAccess.Set(SuperEffect.Scrawl, false);
            window.Content = tile;
            Dispatcher.UIThread.RunJobs();
            Assert.True(gold > CornerBrightness(window, corner) + 40);
        });

    [Fact]
    public void DetachedCornerStopsFollowingAccess() => WithTile(
        () => new FeatureCard { Width = 180, Height = 180, HelpSectionId = "BouncingText" },
        (window, _, tile) =>
        {
            var corner = tile.GetVisualDescendants().OfType<SuperCorner>().Single();
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            SuperAccess.Set(SuperEffect.Scrawl, false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(corner.Lit);
        });

    private static int CornerBrightness(Window window, SuperCorner corner)
    {
        HeadlessFrame.Publish(window);
        using var frame = window.CaptureRenderedFrame()!;
        var point = corner.TranslatePoint(new Point(3, 13), window)!.Value;
        var pixels = new byte[4];
        var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            frame.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), memory, 4, 4);
            System.Runtime.InteropServices.Marshal.Copy(memory, pixels, 0, 4);
            return pixels[0] + pixels[1] + pixels[2];
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }
    }

    private static void Click(Window window, SuperCorner corner, Point point, MouseButton button)
    {
        window.UpdateLayout();
        HeadlessFrame.Publish(window);
        var at = corner.TranslatePoint(point, window)!.Value;
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(at, button);
        window.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WithTile(Func<Control> tileFactory, Action<Window, SettingsService, Control> test) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
        {
            var oldProvider = CoreSettings.ServiceProvider;
            var oldPremium = CoreEntitlement.HasPremiumProvider;
            var oldDenied = CoreEntitlement.ShowDeniedHandler;
            var oldTrying = SuperAccess.TryingProvider;
            var oldOverride = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
            var service = new SettingsService();
            Window? window = null;
            try
            {
                Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
                CoreSettings.ServiceProvider = () => service;
                CoreEntitlement.HasPremiumProvider = () => true;
                SuperAccess.TryingProvider = () => null;
                service.Current.SuperModes = new();
                service.Current.MotionLevel = MotionLevel.Off;
                var tile = tileFactory();
                window = new Window { Content = tile, Width = 220, Height = 220 };
                window.Show();
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                test(window, service, tile);
            }
            finally
            {
                window?.Close();
                Dispatcher.UIThread.RunJobs();
                service.SealForReset();
                CoreSettings.ServiceProvider = oldProvider;
                CoreEntitlement.HasPremiumProvider = oldPremium;
                CoreEntitlement.ShowDeniedHandler = oldDenied;
                SuperAccess.TryingProvider = oldTrying;
                Environment.SetEnvironmentVariable("CCP_SUPER_ALL", oldOverride);
            }
        });
}
