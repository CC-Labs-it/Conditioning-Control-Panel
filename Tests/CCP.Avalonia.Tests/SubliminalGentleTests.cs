using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalGentleTests
{
    [Fact]
    public void Gentle_visuals_remove_the_full_screen_card_background() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var provider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            settings.Current.SubBackgroundTransparent = false;
            settings.Current.GentleVisuals = false;
            Assert.Single(SubliminalOverlayWindow.Build("TEST", settings.Current).Children.OfType<Rectangle>());
            settings.Current.GentleVisuals = true;
            Assert.Empty(SubliminalOverlayWindow.Build("TEST", settings.Current).Children.OfType<Rectangle>());
        }
        finally { CoreSettings.ServiceProvider = provider; }
    });
    [Fact]
    public void Hosted_card_uses_gentle_fades_without_shortening_the_hold() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var provider = CoreSettings.ServiceProvider;
        var configure = SharedOverlayHost.Configure; var transparency = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var scheduler = SubliminalOverlay.Schedule;
        var settings = new SettingsService();
        settings.Current.GentleVisuals = true;
        settings.Current.GentleFadeSeconds = 0.75;
        CoreSettings.ServiceProvider = () => settings;
        Window? stage = null;
        var pending = new List<(Action Run, TimeSpan At)>();
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = _ => true;
        SubliminalOverlay.Schedule = (action, at) => pending.Add((action, at));
        void Advance(int ms)
        {
            foreach (var item in pending.Where(x => x.At <= TimeSpan.FromMilliseconds(ms)).OrderBy(x => x.At).ToArray())
            {
                pending.Remove(item);
                item.Run();
            }
        }
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(100)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children);
            Advance(50);
            Assert.InRange(card.Opacity, 0.03, 0.10);
            Advance(750);
            Assert.Equal(0.8, card.Opacity, 3);
            Advance(850);
            Assert.Equal(0.8, card.Opacity, 3);
            Advance(1225);
            Assert.InRange(card.Opacity, 0.39, 0.42);
            Advance(1600);
            Assert.Empty(canvas.Children);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            SubliminalOverlay.Schedule = scheduler;
            SharedOverlayHost.Configure = configure; SharedOverlayHost.IsTransparent = transparency;
            SharedOverlayHost.Raise = raise;
            CoreSettings.ServiceProvider = provider;
        }
    });
    [Fact]
    public void Classic_card_schedules_gentle_ramps_and_a_late_enough_backstop() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var provider = CoreSettings.ServiceProvider;
        var scheduler = SubliminalOverlay.Schedule;
        var requestFrame = SubliminalOverlayWindow.RequestFrame;
        var settings = new SettingsService();
        settings.Current.GentleVisuals = true;
        settings.Current.GentleFadeSeconds = 0.75;
        CoreSettings.ServiceProvider = () => settings;
        var pending = new List<(Action Run, TimeSpan At)>();
        SubliminalOverlay.Schedule = (action, at) => pending.Add((action, at));
        SubliminalOverlayWindow.RequestFrame = (_, action) => action(TimeSpan.Zero);
        var window = new SubliminalOverlayWindow("TEST");
        try
        {
            window.Show();
            var hold = TimeSpan.FromMilliseconds(100);
            window.Run(0.8, hold);
            Assert.Equal(TimeSpan.FromMilliseconds(1700), SubliminalOverlayWindow.MaxLifetime(hold));
            Assert.Contains(pending, x => x.At == TimeSpan.FromMilliseconds(1700));
            Assert.Contains(pending, x => x.At == TimeSpan.FromMilliseconds(1601));
            Assert.True(pending.Count(x => x.At > TimeSpan.Zero && x.At <= TimeSpan.FromMilliseconds(750)) >= 40);
            Assert.True(pending.Count(x => x.At > TimeSpan.FromMilliseconds(850) && x.At <= TimeSpan.FromMilliseconds(1600)) >= 40);
        }
        finally
        {
            window.Close();
            SubliminalOverlayWindow.RequestFrame = requestFrame;
            SubliminalOverlay.Schedule = scheduler;
            CoreSettings.ServiceProvider = provider;
        }
    });
}
