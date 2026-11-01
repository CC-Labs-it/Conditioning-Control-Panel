using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalHostedFrameTests
{
    // WPF SubliminalService.cs:842-873: the hold follows the complete fade-in.
    [Fact]
    public void Delayed_first_frame_keeps_the_full_fade_and_hold() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var provider = CoreSettings.ServiceProvider;
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var schedule = SubliminalOverlay.Schedule;
        var request = SubliminalOverlayWindow.RequestFrame;
        var service = new SettingsService();
        var timers = new List<(double At, Action Run)>();
        var frames = new List<Action<TimeSpan>>();
        var now = 0.0;
        Window? stage = null;
        void Advance(double target)
        {
            while (true)
            {
                var due = timers.Where(t => t.At <= target).OrderBy(t => t.At).ToArray();
                if (due.Length == 0) break;
                timers.Remove(due[0]); now = due[0].At; due[0].Run();
            }
            now = target;
        }
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.GentleVisuals = false;
            service.Current.SubliminalFont = "DejaVu Sans";
            SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            SharedOverlayHost.Raise = _ => true;
            SubliminalOverlay.Schedule = (action, delay) => timers.Add((now + delay.TotalMilliseconds, action));
            SubliminalOverlayWindow.RequestFrame = (top, callback) => { Assert.Same(stage, top); frames.Add(callback); };

            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(100)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children);
            Advance(500); // later than the entire old fade and hold
            Assert.True(stage.IsVisible);
            Assert.Same(card, Assert.Single(canvas.Children));
            Assert.Equal(0, card.Opacity);
            service.Current.GentleVisuals = true; // the pending card keeps its resolved WPF fade
            service.Current.GentleFadeSeconds = 0.75;
            var firstFrame = Assert.Single(frames);
            firstFrame(TimeSpan.FromMilliseconds(now));
            var timerCount = timers.Count;
            firstFrame(TimeSpan.FromMilliseconds(now)); // a duplicate frame callback cannot restart the envelope
            Assert.Equal(timerCount, timers.Count);
            Advance(550);
            Assert.Equal(0.8, card.Opacity, 3);
            Advance(650);
            Assert.Equal(0.8, card.Opacity, 3);
            Advance(675);
            Assert.InRange(card.Opacity, 0.01, 0.79);
            Advance(700);
            Assert.Empty(canvas.Children);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            CoreSettings.ServiceProvider = provider;
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparent;
            SharedOverlayHost.Raise = raise;
            SubliminalOverlay.Schedule = schedule;
            SubliminalOverlayWindow.RequestFrame = request;
        }
    });

    [Theory]
    [InlineData("timeout")]
    [InlineData("stop-before-frame")]
    [InlineData("stop-after-frame")]
    public void Timeout_and_stop_retire_cards_without_late_frames_reviving_them(string reason) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var provider = CoreSettings.ServiceProvider;
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var schedule = SubliminalOverlay.Schedule;
        var request = SubliminalOverlayWindow.RequestFrame;
        var service = new SettingsService();
        var timers = new List<(double At, Action Run)>();
        var frames = new List<Action<TimeSpan>>();
        var now = 0.0;
        Window? stage = null;
        void Advance(double target)
        {
            while (true)
            {
                var due = timers.Where(t => t.At <= target).OrderBy(t => t.At).ToArray();
                if (due.Length == 0) break;
                timers.Remove(due[0]); now = due[0].At; due[0].Run();
            }
            now = target;
        }
        try
        {
            CoreSettings.ServiceProvider = () => service;
            service.Current.GentleVisuals = false;
            service.Current.SubliminalFont = "DejaVu Sans";
            SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            SharedOverlayHost.Raise = _ => true;
            SubliminalOverlay.Schedule = (action, delay) => timers.Add((now + delay.TotalMilliseconds, action));
            SubliminalOverlayWindow.RequestFrame = (top, callback) => { Assert.Same(stage, top); frames.Add(callback); };
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(100)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children);
            var firstFrame = Assert.Single(frames);
            if (reason == "stop-after-frame") firstFrame(TimeSpan.Zero);
            if (reason == "timeout") Advance(3000);
            else SubliminalOverlay.CloseAll();
            Assert.Empty(canvas.Children);
            Assert.False(stage.IsVisible);
            var timerCount = timers.Count;
            if (reason != "stop-after-frame") firstFrame(TimeSpan.FromMilliseconds(now));
            Assert.Equal(timerCount, timers.Count);
            if (reason == "stop-after-frame")
            {
                Advance(20);
                Assert.Equal(0, card.Opacity);
            }
            Advance(6000);
            Assert.Empty(canvas.Children);
            Assert.False(stage.IsVisible);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            CoreSettings.ServiceProvider = provider;
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparent;
            SharedOverlayHost.Raise = raise;
            SubliminalOverlay.Schedule = schedule;
            SubliminalOverlayWindow.RequestFrame = request;
        }
    });
}
