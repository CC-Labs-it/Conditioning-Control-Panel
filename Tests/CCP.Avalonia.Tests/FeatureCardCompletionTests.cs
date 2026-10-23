using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FeatureCardCompletionTests
{
    [Fact]
    public void ClickTipsHoverGracePinEscapeAndPanicFollowTheReference() => WithDashboard((window, dash) =>
    {
        var anchor = dash.FindControl<Button>("ClickChoiceAnchor")!;
        Assert.NotNull(anchor); // missing on the base
        var popup = dash.FindControl<Popup>("ClickChoicePopup")!;
        var body = dash.FindControl<Border>("ClickChoiceBody")!;
        var check = dash.FindControl<CheckBox>("InvertDashboardClicks")!;
        var timer = (DispatcherTimer)typeof(SettingsTabView).GetField("_clickChoiceClose", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dash)!;
        Assert.Equal(TimeSpan.FromMilliseconds(450), timer.Interval);
        Assert.True(EscapeClaim.InASurface(check));
        Assert.False(popup.IsOpen);

        Pointer(anchor, window, true);
        Assert.True(popup.IsOpen);
        Assert.False(popup.IsLightDismissEnabled); // hover must not capture/dismiss
        Pointer(anchor, window, false);
        Assert.True(timer.IsEnabled);
        Assert.True(popup.IsOpen); // crossing the gap gets a grace period
        Pointer(body, window, true);
        Assert.False(timer.IsEnabled);
        Pointer(body, window, false);
        Elapse(dash);
        Assert.False(popup.IsOpen);

        anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.True(popup.IsLightDismissEnabled);
        Assert.True(check.IsFocused);
        Pointer(body, window, false);
        Assert.False(timer.IsEnabled); // pinned survives pointer leave
        EscapeClaim.Claimed(DateTime.UtcNow);
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        check.RaiseEvent(escape);
        Assert.True(escape.Handled);
        Assert.False(popup.IsOpen);
        Assert.True(anchor.IsFocused);
        Assert.False(EscapeClaim.OnItsWay(DateTime.UtcNow));

        anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PanicSurfaces.All.Single(s => s.Id == "dashboard-click-choice").Stop(null);
        Assert.False(popup.IsOpen);
        Pointer(anchor, window, true);
        dash.IsVisible = false;
        Assert.False(popup.IsOpen);
        Assert.False(timer.IsEnabled);
        dash.IsVisible = true;
        Pointer(anchor, window, true);
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public void PreferenceWritesLiveSettingsAndRepaintsCopyWithoutWritingOnOpen() => WithDashboard((window, dash) =>
    {
        var anchor = dash.FindControl<Button>("ClickChoiceAnchor")!;
        Assert.NotNull(anchor);
        var popup = dash.FindControl<Popup>("ClickChoicePopup")!;
        var check = dash.FindControl<CheckBox>("InvertDashboardClicks")!;
        var description = dash.FindControl<TextBlock>("ClickChoiceDescription")!;
        var hint = dash.FindControl<TextBlock>("DashToggleHint")!;
        Pointer(anchor, window, true);
        Assert.False(CoreSettings.Current.DashboardInvertClicks);
        Assert.False((bool)typeof(SettingsService).GetField("_savePending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(CoreSettings.Service)!);
        Assert.False(check.IsChecked);
        Assert.Equal(Loc.Get("dash_click_default"), description.Text);
        Assert.Equal(Loc.Get("dash_toggle_hint"), hint.Text);
        check.IsChecked = true;
        Assert.True(CoreSettings.Current.DashboardInvertClicks);
        Assert.True(popup.IsLightDismissEnabled); // editing pins a hover-open popup too
        Assert.Equal(Loc.Get("dash_click_swapped"), description.Text);
        Assert.Equal(description.Text, hint.Text);
        Assert.True((bool)typeof(SettingsService).GetField("_savePending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(CoreSettings.Service)!);
        CoreSettings.Service!.SaveImmediate();
        var reloaded = new SettingsService();
        Assert.True(reloaded.Current.DashboardInvertClicks);
        popup.IsOpen = false;
        anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(check.IsChecked);
        check.IsChecked = false;
        Assert.False(CoreSettings.Current.DashboardInvertClicks);
        Assert.Equal(Loc.Get("dash_click_default"), description.Text);
        Assert.Equal(Loc.Get("dash_toggle_hint"), hint.Text);
        popup.IsOpen = false;
        Assert.False(popup.IsLightDismissEnabled); // Closed resets the pinned mode
        var card = dash.FindControl<FeatureCard>("CardFlash")!;
        int opens = 0, toggles = 0;
        card.Click += (_, _) => opens++;
        card.ToggleRequested += (_, _) => toggles++;
        // A user changes the preference, closes the popup and then uses the actual Home tile.
        anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check.IsChecked = true;
        popup.IsOpen = false;
        var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Assert.Equal((1, 1), (opens, toggles));
    });

    [Fact]
    public void LogoHintRetiresAfterThreeWallTogglesAndStaysRetired() => WithDashboard((window, dash) =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var hint = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<TextBlock>("DashToggleHint")!;
            Assert.NotNull(hint);
            for (int uses = 1; uses <= 4; uses++)
            {
                shell.ToggleWallFeature("flash");
                Assert.Equal(Math.Min(uses, 3), CoreSettings.Current.DashboardToggleHintUses);
                Assert.Equal(uses < 3, hint.IsVisible);
            }
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData("fx.flash.drift_bounce", true, false)]
    [InlineData("fx.flash.pendulum", true, false)]
    [InlineData("fx.jackpot_remix", true, false)]
    [InlineData("fx.bubble.rain", false, true)]
    [InlineData("fx.bubble.spiral_in", false, true)]
    [InlineData("other.prize", false, false)]
    public void DashboardBadgesReadOwnershipAndRecheckOnReturn(string grant, bool flash, bool bubble) => WithDashboard((window, dash) =>
    {
        var before = PrizeOwnership.IsGranted;
        try
        {
            dash.IsVisible = false;
            PrizeOwnership.IsGranted = id => id == grant;
            // Settings never grant the badge; this exercises the same Home visibility path as navigation.
            CoreSettings.Current.FlashEnabled = CoreSettings.Current.BubblesEnabled = true;
            dash.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(flash, dash.FindControl<FeatureCard>("CardFlash")!.ShowV2Badge);
            Assert.Equal(bubble, dash.FindControl<FeatureCard>("CardBubblePop")!.ShowV2Badge);
            dash.IsVisible = false;
            PrizeOwnership.IsGranted = _ => false;
            dash.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(dash.FindControl<FeatureCard>("CardFlash")!.ShowV2Badge);
            Assert.False(dash.FindControl<FeatureCard>("CardBubblePop")!.ShowV2Badge);
        }
        finally { PrizeOwnership.IsGranted = before; }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreferenceSwapsBothSplitHalvesOnlyOnDashboard(bool halfA) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureAvalonia();
        bool before = CoreSettings.Current.DashboardInvertClicks;
        var card = new SplitFeatureCard { Width = 180, Height = 140, DashboardDepth = true };
        var window = new Window { Content = card, Width = 180, Height = 140 };
        int opens = 0, toggles = 0;
        card.ClickA += (_, _) => { Assert.True(halfA); opens++; };
        card.ClickB += (_, _) => { Assert.False(halfA); opens++; };
        card.ToggleA += (_, _) => { Assert.True(halfA); toggles++; };
        card.ToggleB += (_, _) => { Assert.False(halfA); toggles++; };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var at = halfA ? new Point(25, 25) : new Point(155, 115);
            CoreSettings.Current.DashboardInvertClicks = true;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal((0, 1), (opens, toggles));
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Assert.Equal((1, 1), (opens, toggles));
            card.DashboardDepth = false;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal((2, 1), (opens, toggles));
        }
        finally { window.Close(); CoreSettings.Current.DashboardInvertClicks = before; }
    });

    private static void Pointer(Control target, Window window, bool enter) => target.RaiseEvent(new PointerEventArgs(
        enter ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, target,
        new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));

    private static void Elapse(SettingsTabView dash) => typeof(SettingsTabView)
        .GetMethod("ClickChoiceCloseElapsed", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dash, null);

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void WithDashboard(Action<Window, SettingsTabView> check) => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureAvalonia();
        var before = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.DashboardInvertClicks = false;
        service.Current.DashboardToggleHintUses = 0;
        service.SaveImmediate();
        var dash = new SettingsTabView();
        var window = new Window { Content = dash, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            check(window, dash);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = before;
        }
    });
}
