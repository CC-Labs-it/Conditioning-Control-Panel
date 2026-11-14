using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DashboardClickChoiceTests
{
    [Fact]
    public Task PreferencePopupPersistsPinsClaimsEscapeAndClosesWhenHidden() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var saved = (service.Current.DashboardInvertClicks, service.Current.DashboardToggleHintUses);
        service.Current.DashboardInvertClicks = false; service.Current.DashboardToggleHintUses = 0;
        service.Current.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        shell.Show(); Dispatcher.UIThread.RunJobs();
        var dash = shell.SettingsPage!;
        T F<T>(string name) where T : Control => dash.FindControl<T>(name)!;
        Point AnchorCentre() { var a = F<Button>("ClickChoiceAnchor"); return a.TranslatePoint(new Point(a.Bounds.Width / 2, a.Bounds.Height / 2), shell)!.Value; }
        void Hover() { shell.MouseMove(AnchorCentre()); Dispatcher.UIThread.RunJobs(); }
        void Leave() { shell.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs(); }
        void ClickAnchor() { var pt = AnchorCentre(); shell.MouseMove(pt); shell.MouseDown(pt, MouseButton.Left); shell.MouseUp(pt, MouseButton.Left); Dispatcher.UIThread.RunJobs(); }
        try
        {
            dash.SetFavoritesDrawer(true, animate: false);
            Hover(); // real pointer over the "Left or right?" anchor, not a direct call
            Assert.True(F<Popup>("ClickChoicePopup").IsOpen);
            Assert.False(F<Popup>("ClickChoicePopup").IsLightDismissEnabled);
            Assert.Equal(Loc.Get("dash_click_default"), F<TextBlock>("ClickChoiceDescription").Text);
            Leave(); dash.CloseClickChoiceAfterDelay();
            Assert.False(F<Popup>("ClickChoicePopup").IsOpen);
            ClickAnchor();
            Assert.True(F<Popup>("ClickChoicePopup").IsOpen);
            Assert.True(F<Popup>("ClickChoicePopup").IsLightDismissEnabled);
            F<CheckBox>("InvertDashboardClicks").IsChecked = true;
            Assert.True(service.Current.DashboardInvertClicks);
            Assert.Equal(Loc.Get("dash_click_swapped"), F<TextBlock>("ClickChoiceDescription").Text);
            Assert.Equal(Loc.Get("dash_click_swapped"), F<TextBlock>("DashToggleHint").Text);
            service.Current.DashboardInvertClicks = false; shell.RefreshFavoritesRail();
            Assert.False(F<CheckBox>("InvertDashboardClicks").IsChecked); // profile/language rail refresh follows truth
            service.Current.DashboardInvertClicks = true;
            shell.RefreshDashboardToggleHint(); // the incoming restore/counter refresh must honor the saved gesture
            Assert.True(F<CheckBox>("InvertDashboardClicks").IsChecked);
            Assert.Equal(Loc.Get("dash_click_swapped"), F<TextBlock>("DashToggleHint").Text);
            F<CheckBox>("InvertDashboardClicks").IsChecked = true;
            if (Environment.GetEnvironmentVariable("CCP_SETTINGS_RENDER_DIR") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = shell.CaptureRenderedFrame();
                Assert.NotNull(frame); frame.Save(Path.Combine(output, "DashboardClickTips.png"));
                var body = F<Border>("ClickChoiceBody");
                using var popupFrame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(body.Bounds.Width), (int)Math.Ceiling(body.Bounds.Height)), new Vector(96, 96));
                popupFrame.Render(body); popupFrame.Save(Path.Combine(output, "ClickChoicePopup.png"));
            }
            dash.CloseClickChoiceAfterDelay();
            Assert.True(F<Popup>("ClickChoicePopup").IsOpen); // pinned never expires on pointer leave
            Assert.True(EscapeClaim.InASurface(F<CheckBox>("InvertDashboardClicks")));
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            dash.ClickChoiceKeyDown(dash, escape);
            Assert.True(escape.Handled);
            Assert.False(F<Popup>("ClickChoicePopup").IsOpen);
            dash.OpenClickChoice(pinned: true); dash.IsVisible = false;
            Assert.False(F<Popup>("ClickChoicePopup").IsOpen);
            dash.IsVisible = true;
            for (int i = 0; i < 4; i++) shell.ToggleWallFeature("flash");
            Assert.Equal(3, service.Current.DashboardToggleHintUses);
            Assert.False(F<TextBlock>("DashToggleHint").IsVisible);
            service.SaveImmediate();
            Assert.True(new SettingsService().Current.DashboardInvertClicks);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            (service.Current.DashboardInvertClicks, service.Current.DashboardToggleHintUses) = saved;
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SingleAndSplitCardsUseSavedGestureWithoutSwappingNavigationOnlyCards() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current; var old = s.DashboardInvertClicks; s.DashboardInvertClicks = true;
        var card = new FeatureCard { DashboardDepth = true, DimWhenInactive = true, Width = 200, Height = 200 };
        var split = new SplitFeatureCard { DashboardDepth = true, Width = 200, Height = 200 };
        var window = new Window { Width = 450, Height = 240, Content = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { card, split } } };
        int opens = 0, toggles = 0, splitOpens = 0, splitToggles = 0;
        card.Click += (_, _) => opens++; card.ToggleRequested += (_, _) => toggles++;
        split.ClickA += (_, _) => splitOpens++; split.ToggleA += (_, _) => splitToggles++;
        window.Show(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        try
        {
            void Click(Control c, MouseButton button)
            {
                var pt = c.TranslatePoint(new Point(20, 20), window)!.Value;
                window.MouseDown(pt, button); window.MouseUp(pt, button); Dispatcher.UIThread.RunJobs();
            }
            Click(card, MouseButton.Left); Assert.Equal((0, 1), (opens, toggles));
            Click(card, MouseButton.Right);
            Assert.Equal((1, 1), (opens, toggles));
            card.IsLocked = true; Click(card, MouseButton.Left); Assert.Equal(1, toggles);
            card.IsLocked = false; card.DimWhenInactive = false; Click(card, MouseButton.Left); Assert.Equal(2, opens);
            Click(split, MouseButton.Left); Assert.Equal((0, 1), (splitOpens, splitToggles));
            Click(split, MouseButton.Right); Assert.Equal((1, 1), (splitOpens, splitToggles));
            split.DashboardDepth = false; Click(split, MouseButton.Left); Assert.Equal(2, splitOpens);
        }
        finally { window.Close(); s.DashboardInvertClicks = old; }
        return Task.CompletedTask;
    });
}
