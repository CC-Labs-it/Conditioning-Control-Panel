using System.Threading.Tasks;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FavoritesClickChoiceTests
{
    [Fact]
    public Task ClickTipsPersistsPreferenceAndEscapeClosesWithoutPanic() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previousProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        // Normal Home after the first-run and upgrade gates have been completed.
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 1;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            var dash = shell.SettingsPage!;
            var anchor = dash.FindControl<Button>("ClickChoiceAnchor");
            Assert.NotNull(anchor);
            var popup = dash.FindControl<Popup>("ClickChoicePopup")!;
            var choice = dash.FindControl<CheckBox>("InvertDashboardClicks")!;
            CoreSettings.Current.DashboardInvertClicks = false;
            dash.SetFavoritesDrawer(true, animate: false);
            Dispatcher.UIThread.RunJobs();
            shell.MouseMove(anchor.TranslatePoint(new Point(4, 4), shell)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.False(popup.IsLightDismissEnabled); // hovering must not capture the pointer
            dash.CloseClickChoiceAfterDelay();
            Assert.True(popup.IsOpen);                 // still on the anchor
            shell.MouseMove(new Point(1, 1));
            Dispatcher.UIThread.RunJobs();
            dash.CloseClickChoiceAfterDelay();
            Assert.False(popup.IsOpen);
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.True(popup.IsLightDismissEnabled);
            var body = dash.FindControl<Border>("ClickChoiceBody")!;
            Assert.True(body.Bounds.Width > 0 && body.Bounds.Height > 0);
            Assert.True(EscapeClaim.InASurface(choice));
            Assert.Equal(Loc.Get("dash_click_default"), dash.FindControl<TextBlock>("ClickChoiceDescription")!.Text);
            SettingsService.DisarmAllPendingSaves();
            Assert.False(settings.HasPendingSave);
            choice.IsChecked = true;
            Assert.True(settings.HasPendingSave); // UI requested the real service's debounced save
            Assert.True(CoreSettings.Current.DashboardInvertClicks);
            Assert.Equal(Loc.Get("dash_click_swapped"), dash.FindControl<TextBlock>("ClickChoiceDescription")!.Text);
            choice.Focus();
            shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); // real key routing
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.True(anchor.IsFocused);
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(choice.IsChecked);
            dash.IsVisible = false;
            Assert.False(popup.IsOpen);
        }
        finally
        {
            shell?.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previousProvider;
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task InvertedClicksSwapToggleTilesButKeepPageLinks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        bool old = CoreSettings.Current.DashboardInvertClicks;
        var card = new ConditioningControlPanel.Avalonia.Views.Features.FeatureCard { DashboardDepth = true, DimWhenInactive = true };
        int opens = 0, toggles = 0;
        card.Click += (_, _) => opens++;
        card.ToggleRequested += (_, _) => toggles++;
        var split = new ConditioningControlPanel.Avalonia.Views.Features.SplitFeatureCard { DashboardDepth = true };
        split.ClickA += (_, _) => opens++;
        split.ClickB += (_, _) => opens++;
        split.ToggleA += (_, _) => toggles++;
        split.ToggleB += (_, _) => toggles++;
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel
            { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { card, split } } };
        card.Width = split.Width = 190;
        card.Height = split.Height = 190;
        window.Show();
        try
        {
            void Click(Control control, MouseButton button)
            {
                var at = control.TranslatePoint(new Point(20, 20), window)!.Value;
                window.MouseDown(at, button);
                window.MouseUp(at, button);
                Dispatcher.UIThread.RunJobs();
            }
            CoreSettings.Current.DashboardInvertClicks = false;
            Click(card, MouseButton.Left);
            Click(card, MouseButton.Right);
            Assert.Equal((1, 1), (opens, toggles));
            CoreSettings.Current.DashboardInvertClicks = true;
            Click(card, MouseButton.Left);
            Assert.Equal((1, 2), (opens, toggles));
            Click(card, MouseButton.Right);
            Assert.Equal((2, 2), (opens, toggles));
            card.IsLocked = true;
            Click(card, MouseButton.Left);
            Assert.Equal((2, 2), (opens, toggles)); // locked cannot toggle
            Click(card, MouseButton.Right);
            Assert.Equal((3, 2), (opens, toggles)); // opening options remains available
            card.IsLocked = false;
            card.DimWhenInactive = false;         // page links keep left=open
            Click(card, MouseButton.Left);
            Assert.Equal((4, 2), (opens, toggles));
            Click(split, MouseButton.Left);
            Assert.Equal((4, 3), (opens, toggles));
            Click(split, MouseButton.Right);
            Assert.Equal((5, 3), (opens, toggles));
        }
        finally { window.Close(); CoreSettings.Current.DashboardInvertClicks = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task NewPinGlowsItsChip() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previousProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;                       // P52/P76: no first-run or intro card mid-test
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.RailFavorites.Clear();
        s.MotionLevel = MotionLevel.Reduced;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            shell.SettingsPage!.SetFavoritesDrawer(true, animate: false);
            shell.TogglePinned("tab.haptics");
            var chip = shell.SettingsPage.FindControl<StackPanel>("FavoritesList")!.Children
                .OfType<Button>().Single(c => Equals(c.Tag, "tab.haptics"));
            bool Glowing() => AdornerLayer.GetAdornerLayer(chip)!.Children.OfType<NavGlowRing>()
                .Any(ring => AdornerLayer.GetAdornedElement(ring) == chip);
            var sw = System.Diagnostics.Stopwatch.StartNew();   // P73: bounded by time, not turns
            while (!Glowing() && sw.Elapsed < System.TimeSpan.FromSeconds(10)) Dispatcher.UIThread.RunJobs();
            Assert.True(Glowing(), "pinned chip never glowed");
        }
        finally
        {
            shell?.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previousProvider;
        }
        return Task.CompletedTask;
    });
}
