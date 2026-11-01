using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalCompositorRouteTests
{
    // WPF SubliminalService.cs:650-661 sends both modes to the same compositor as the tint.
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void Show_routes_modes_using_the_renderer_setting(bool solid, bool unified, bool hosted) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var available = SubliminalOverlay.IsAvailable;
        var query = ScreenList.QueryOverride;
        var provider = CoreSettings.ServiceProvider;
        var originalSettings = CoreSettings.Current;
        var originalValues = (originalSettings.SubliminalSolidMode, originalSettings.UnifiedOverlayHost,
            originalSettings.SubliminalStealsFocus, originalSettings.SubliminalFont,
            originalSettings.DualMonitorEnabled, originalSettings.GlobalTargetMonitor);
        var classic = SubliminalOverlay.ConfigureClassic;
        var frame = SubliminalOverlayWindow.RequestFrame;
        var windows = new List<Window>();
        var stages = new List<Window>();
        var service = new SettingsService();
        var owner = new Window();
        try
        {
            SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            SharedOverlayHost.Raise = _ => true;
            SubliminalOverlay.IsAvailable = () => true;
            ScreenList.QueryOverride = _ => new[] { screen };
            SubliminalOverlay.ConfigureClassic = (w, _) => { windows.Add(w); w.Width = 300; w.Height = 200; return true; };
            SubliminalOverlayWindow.RequestFrame = (_, _) => { };
            CoreSettings.ServiceProvider = () => service;
            var settings = service.Current;
            settings.SubliminalSolidMode = solid;
            settings.UnifiedOverlayHost = unified;
            settings.SubliminalStealsFocus = false;
            settings.SubliminalFont = "DejaVu Sans";
            settings.DualMonitorEnabled = false;
            settings.GlobalTargetMonitor = -1;
            owner.Show();
            Assert.True(PinkFilterOverlay.ShowHosted(bounds, 1, 2, 3, 0.4));
            SubliminalOverlay.Show(owner, "FIRST");
            var stage = Assert.Single(stages);
            var canvas = Assert.IsType<Grid>(stage.Content);
            var tint = Assert.Single(canvas.Children.OfType<Border>());
            if (!hosted)
            {
                Assert.Empty(canvas.Children.OfType<Grid>());
                var window = Assert.Single(windows);
                Assert.True(window.IsVisible);
                Assert.False(window.IsHitTestVisible);
                Assert.Contains(Assert.IsType<Grid>(window.Content).Children.OfType<TextBlock>(), t => t.Text == "FIRST");
                SubliminalOverlay.CloseAll();
                Assert.False(window.IsVisible);
            }
            else
            {
                Assert.Empty(windows);
                var card = Assert.Single(canvas.Children.OfType<Grid>());
                Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
                Assert.True(tint.ZIndex > card.ZIndex);
                SubliminalOverlay.Show(owner, "SECOND");
                Assert.Same(stage, Assert.Single(stages));
                Assert.Single(canvas.Children.OfType<Grid>());
                Assert.DoesNotContain(card, canvas.Children);
                SubliminalOverlay.CloseAll();
                Assert.True(stage.IsVisible);
                Assert.Same(tint, Assert.Single(canvas.Children));
                PinkFilterOverlay.CloseAll();
                Assert.False(stage.IsVisible);
            }
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            PinkFilterOverlay.CloseAll();
            owner.Close();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparent;
            SharedOverlayHost.Raise = raise;
            SubliminalOverlay.IsAvailable = available;
            ScreenList.QueryOverride = query;
            SubliminalOverlay.ConfigureClassic = classic;
            SubliminalOverlayWindow.RequestFrame = frame;
            CoreSettings.ServiceProvider = provider;
        }
        Assert.Same(originalSettings, CoreSettings.Current);
        Assert.Equal(originalValues, (originalSettings.SubliminalSolidMode, originalSettings.UnifiedOverlayHost,
            originalSettings.SubliminalStealsFocus, originalSettings.SubliminalFont,
            originalSettings.DualMonitorEnabled, originalSettings.GlobalTargetMonitor));
    });

    // WPF SubliminalService.cs:650-690: focus, video and host failure use a classic card.
    [Theory]
    [InlineData("focus")]
    [InlineData("video")]
    [InlineData("refused")]
    [InlineData("opaque")]
    public void Fallback_presents_a_classic_card(string reason) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var available = SubliminalOverlay.IsAvailable;
        var classic = SubliminalOverlay.ConfigureClassic;
        var videos = SubliminalOverlay.VideoWindowCount;
        var focus = SubliminalOverlay.ActivateCard;
        var frame = SubliminalOverlayWindow.RequestFrame;
        var query = ScreenList.QueryOverride;
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var cards = new List<Window>();
        var stages = new List<Window>();
        var activations = 0;
        var owner = new Window();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.UnifiedOverlayHost = true;
            service.Current.SubliminalSolidMode = false;
            service.Current.SubliminalStealsFocus = reason == "focus";
            service.Current.SubliminalFont = "DejaVu Sans";
            service.Current.GlobalTargetMonitor = -1;
            service.Current.DualMonitorEnabled = false;
            ScreenList.QueryOverride = _ => new[] { screen };
            SubliminalOverlay.IsAvailable = () => true;
            SubliminalOverlay.VideoWindowCount = () => reason == "video" ? 1 : 0;
            SubliminalOverlay.ConfigureClassic = (w, _) => { cards.Add(w); w.Width = 300; w.Height = 200; return true; };
            SubliminalOverlay.ActivateCard = _ => activations++;
            SubliminalOverlayWindow.RequestFrame = (_, _) => { };
            SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return reason != "refused"; };
            SharedOverlayHost.IsTransparent = _ => reason != "opaque";
            owner.Show();

            SubliminalOverlay.Show(owner, "FALLBACK");
            var card = Assert.Single(cards);
            Assert.True(card.IsVisible);
            Assert.False(card.IsHitTestVisible);
            Assert.Contains(Assert.IsType<Grid>(card.Content).Children.OfType<TextBlock>(), t => t.Text == "FALLBACK");
            Assert.Equal(reason == "focus" ? 1 : 0, activations);
            Assert.Equal(reason is "refused" or "opaque" ? 1 : 0, stages.Count);
            Assert.All(stages, stage => Assert.False(stage.IsVisible));
            SubliminalOverlay.CloseAll();
            Assert.False(card.IsVisible);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            owner.Close();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparent;
            SubliminalOverlay.IsAvailable = available;
            SubliminalOverlay.ConfigureClassic = classic;
            SubliminalOverlay.VideoWindowCount = videos;
            SubliminalOverlay.ActivateCard = focus;
            SubliminalOverlayWindow.RequestFrame = frame;
            ScreenList.QueryOverride = query;
            CoreSettings.ServiceProvider = provider;
        }
    });

    // WPF SubliminalService.cs:865-871 releases the last one-shot host reference.
    [Theory]
    [InlineData(false, false, "classic")]
    [InlineData(true, false, "classic")]
    [InlineData(false, true, "classic")]
    [InlineData(true, true, "classic")]
    [InlineData(false, false, "hosted")]
    [InlineData(true, false, "hosted")]
    [InlineData(false, false, "refused")]
    [InlineData(true, true, "refused")]
    public void Replacement_releases_only_unused_one_shot_leases(bool firstFrame, bool tint, string replacement) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        var (configure, transparent, raise) = (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, SharedOverlayHost.Raise);
        var (available, classic, videos, schedule) = (SubliminalOverlay.IsAvailable, SubliminalOverlay.ConfigureClassic,
            SubliminalOverlay.VideoWindowCount, SubliminalOverlay.Schedule);
        var (frame, query, provider) = (SubliminalOverlayWindow.RequestFrame, ScreenList.QueryOverride, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        var stages = new List<Window>();
        var cards = new List<Window>();
        var frames = new List<Action<TimeSpan>>();
        var timers = new List<Action>();
        var owner = new Window();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.UnifiedOverlayHost = true;
            service.Current.SubliminalSolidMode = false;
            service.Current.SubliminalStealsFocus = false;
            service.Current.SubliminalFont = "DejaVu Sans";
            service.Current.DualMonitorEnabled = false;
            service.Current.GlobalTargetMonitor = -1;
            SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            SharedOverlayHost.Raise = _ => true;
            SubliminalOverlay.IsAvailable = () => true;
            SubliminalOverlay.VideoWindowCount = () => 0;
            SubliminalOverlay.ConfigureClassic = (w, _) => { cards.Add(w); w.Width = 300; w.Height = 200; return replacement != "refused"; };
            SubliminalOverlay.Schedule = (action, _) => timers.Add(action);
            SubliminalOverlayWindow.RequestFrame = (_, callback) => frames.Add(callback);
            ScreenList.QueryOverride = _ => new[] { screen };
            owner.Show();
            Assert.False(CoreSubliminal.IsRunning);
            if (tint) Assert.True(PinkFilterOverlay.ShowHosted(bounds, 1, 2, 3, 0.4));
            SubliminalOverlay.Show(owner, "FIRST");
            var stage = Assert.Single(stages);
            var canvas = Assert.IsType<Grid>(stage.Content);
            var oldFrame = Assert.Single(frames);
            if (firstFrame) oldFrame(TimeSpan.Zero);
            var oldTimers = timers.ToArray();

            service.Current.UnifiedOverlayHost = replacement == "hosted";
            SubliminalOverlay.Show(owner, "SECOND");
            if (replacement == "hosted")
            {
                Assert.Empty(cards);
                Assert.Same(stage, Assert.Single(stages));
                Assert.Single(canvas.Children.OfType<Grid>());
            }
            else
            {
                var card = Assert.Single(cards);
                Assert.Equal(replacement == "classic", card.IsVisible);
                Assert.Empty(canvas.Children.OfType<Grid>());
            }
            Assert.Equal(tint || replacement == "hosted", stage.IsVisible);
            var timerCount = timers.Count;
            if (!firstFrame) oldFrame(TimeSpan.Zero);
            foreach (var timer in oldTimers) timer();
            Assert.Equal(timerCount, timers.Count);
            if (replacement == "hosted")
            {
                Assert.True(stage.IsVisible);
                Assert.Single(canvas.Children.OfType<Grid>());
                Assert.Equal(2, frames.Count);
                var currentTimers = timers.Count;
                frames[1](TimeSpan.Zero);
                foreach (var timer in timers.Skip(currentTimers).ToArray()) timer();
            }
            else if (replacement == "classic") Assert.True(cards[0].IsVisible);
            if (tint)
            {
                Assert.True(stage.IsVisible);
                Assert.Single(canvas.Children.OfType<Border>());
                PinkFilterOverlay.CloseAll();
            }
            Assert.False(stage.IsVisible);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            PinkFilterOverlay.CloseAll();
            owner.Close();
            (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, SharedOverlayHost.Raise) = (configure, transparent, raise);
            (SubliminalOverlay.IsAvailable, SubliminalOverlay.ConfigureClassic, SubliminalOverlay.VideoWindowCount, SubliminalOverlay.Schedule) =
                (available, classic, videos, schedule);
            (SubliminalOverlayWindow.RequestFrame, ScreenList.QueryOverride, CoreSettings.ServiceProvider) = (frame, query, provider);
        }
    });
}
