using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LauncherSoundParityTests
{
    [Fact]
    public void SuppressedOutput_PlaysNoLauncherCue()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPlay = CoreAudio.PlayOneShotProvider;
        var oldSuppressed = CoreAudio.IsOutputSuppressedProvider;
        var settings = new SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.LauncherSoundEnabled = true;
            settings.Current.MasterVolume = 50;
            int played = 0;
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => played++;
            CoreAudio.IsOutputSuppressedProvider = () => true;
            LauncherWindow.LauncherSfx.Click();
            Assert.Equal(0, played);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            LauncherWindow.LauncherSfx.Click();
            Assert.Equal(1, played);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayOneShotProvider = oldPlay;
            CoreAudio.IsOutputSuppressedProvider = oldSuppressed;
        }
    }

    [Fact]
    public void FourthHover_CutsOldestVoice_AndFinishedVoicesDoNotCount()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPlay = CoreAudio.PlayStoppableProvider;
        var oldClock = LauncherWindow.LauncherSfx.Clock;
        var settings = new SettingsService();
        var stopped = new List<int>();
        var finished = new List<Action>();
        var clock = new Clock();
        int serial = 0;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.LauncherSoundEnabled = true;
            settings.Current.MasterVolume = 50;
            LauncherWindow.LauncherSfx.Clock = clock;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, done) =>
            {
                int id = ++serial;
                finished.Add(done!);
                return () => stopped.Add(id);
            };
            for (int i = 0; i < 4; i++) { clock.Now += TimeSpan.FromSeconds(1); LauncherWindow.LauncherSfx.Hover(); }
            Assert.Equal(4, serial);
            Assert.Equal(new[] { 1 }, stopped);
            finished[1]();
            clock.Now += TimeSpan.FromSeconds(1);
            LauncherWindow.LauncherSfx.Hover();
            Assert.Equal(new[] { 1 }, stopped);
        }
        finally
        {
            foreach (var done in finished) done();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayStoppableProvider = oldPlay;
            LauncherWindow.LauncherSfx.Clock = oldClock;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
