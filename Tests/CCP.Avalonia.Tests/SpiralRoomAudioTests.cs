using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRoomAudioTests
{
    [Fact]
    public void CuesRespectGatesFallbackOrderMixAndFailures()
    {
        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldResolve = SpiralRoomAudio.Resolve;
        var oldFuse = App.DescentCountdown;
        using var fuse = new DescentCountdownService();
        var requested = new List<string>();
        var played = new List<(string, float, string)>();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            App.DescentCountdown = fuse;
            CoreAudio.PlayOneShotProvider = (p, v, t, _, _) => played.Add((p, v, t));
            SpiralRoomAudio.Resolve = name => { requested.Add(name); return name == "ui_deepen" ? "fixture" : null; };
            settings.Current.MasterVolume = 50;
            settings.Current.DescentCountdownAudio = true;
            SpiralRoomAudio.PlayFogEntry();
            Assert.Equal(new[] { "descent_fog_enter", "ui_deepen" }, requested);
            Assert.Equal(("fixture", 0.21f, "descent-fog-enter"), Assert.Single(played));
            requested.Clear(); played.Clear();
            SpiralRoomAudio.Resolve = name => { requested.Add(name); return "dedicated"; };
            SpiralRoomAudio.PlaySplashOpen();
            Assert.Equal(new[] { "descent_spiral_open" }, requested);
            Assert.Equal(("dedicated", 0.17f, "descent-spiral-open"), Assert.Single(played));
            requested.Clear(); played.Clear();
            settings.Current.DescentCountdownAudio = false;
            SpiralRoomAudio.PlayFogEntry();
            settings.Current.DescentCountdownAudio = true;
            settings.Current.MasterVolume = 0;
            SpiralRoomAudio.PlaySplashOpen();
            Assert.Empty(requested); Assert.Empty(played);
            settings.Current.MasterVolume = 100;
            SpiralRoomAudio.Resolve = _ => null;
            SpiralRoomAudio.PlayFogEntry();
            SpiralRoomAudio.Resolve = _ => throw new InvalidOperationException("fixture");
            SpiralRoomAudio.PlaySplashOpen();
            Assert.Empty(played);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayOneShotProvider = oldAudio;
            SpiralRoomAudio.Resolve = oldResolve;
            App.DescentCountdown = oldFuse;
            settings.SealForReset();
        }
    }

    [Fact]
    public Task RepaintDoesNotRepeatCuesButReentryDoes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldResolve = SpiralRoomAudio.Resolve;
        var oldEngine = SpiralTabView.EngineAvailable;
        var oldStart = SpiralTabView.StartEmbed;
        var played = new List<string>();
        var window = new Window();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.MasterVolume = 100;
            settings.Current.DescentCountdownAudio = true;
            settings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            SpiralRoomAudio.Resolve = _ => "fixture";
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => played.Add(tag);
            SpiralTabView.EngineAvailable = () => true;
            SpiralTabView.StartEmbed = _ => { };
            var tab = new SpiralTabView();
            window.Content = tab;
            window.Show();
            played.Clear();
            tab.ApplyState(SpiralRoomState.Fog);
            tab.ApplyState(SpiralRoomState.Fog);
            Assert.Equal(new[] { "descent-fog-enter" }, played);
            tab.ApplyState(SpiralRoomState.Spiral);
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open" }, played);
            tab.IsVisible = false;
            tab.IsVisible = true;
            played.Clear();
            tab.ApplyState(SpiralRoomState.Fog);
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.Equal(new[] { "descent-fog-enter", "descent-spiral-open" }, played);

            tab.IsVisible = false;
            tab.IsVisible = true;
            played.Clear();
            var attempts = 0;
            SpiralTabView.StartEmbed = _ => { attempts++; throw new InvalidOperationException("fixture"); };
            tab.ApplyState(SpiralRoomState.Spiral);
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.Equal(1, attempts);
            tab.OnTabShown(); // same-tab explicit navigation earns a retry, but no second chime
            tab.ApplyState(SpiralRoomState.Spiral);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { "descent-spiral-open" }, played);
        }
        finally
        {
            window.Close();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayOneShotProvider = oldAudio;
            SpiralRoomAudio.Resolve = oldResolve;
            SpiralTabView.EngineAvailable = oldEngine;
            SpiralTabView.StartEmbed = oldStart;
            settings.SealForReset();
        }
        return Task.CompletedTask;
    });
}
