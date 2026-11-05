using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Sync7AudioTests
{
    [Fact]
    public async Task PanicStopsCompanionClipsWithTheEngineOffAndFinishedClipsAreForgotten()
    {
        var previous = CoreAudio.PlayStoppableProvider;
        var finishes = new List<Action>();
        int stops = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, started, finished) =>
        {
            started?.Invoke(TimeSpan.FromSeconds(10));
            finishes.Add(finished!);
            return () => { stops++; finished?.Invoke(); };
        };
        try
        {
            Assert.True(await CompanionEffects.PlayAudio("first.mp3"));
            finishes[0]();
            Assert.True(await CompanionEffects.PlayAudio("second.mp3"));
            Assert.True(await CompanionEffects.PlayAudio("third.mp3"));
            var panic = PanicSurfaces.All.Single(s => s.Id == "companion-audio");
            panic.Stop(null);
            Assert.Equal(2, stops);
            panic.Stop(null);
            Assert.Equal(2, stops);
            Assert.True(await CompanionEffects.PlayAudio("after.mp3"));
            panic.Stop(null);
            Assert.Equal(3, stops);
        }
        finally { CompanionEffects.StopAudio(); CoreAudio.PlayStoppableProvider = previous; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(32)]
    [InlineData(100)]
    public void MantraTonesUseMasterGainWithoutOpeningAWindow(int master)
    {
        var previous = CoreAudio.PlayOneShotProvider;
        var volume = CoreSettings.Current.MasterVolume;
        float? tone = null;
        CoreSettings.Current.MasterVolume = master;
        CoreAudio.PlayOneShotProvider = (_, v, _, _, _) => tone = v;
        try
        {
            typeof(MantraWindow).GetMethod("PlayTone", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { 440.0, 100 });
            Assert.Equal(0.15f * master / 100f, tone!.Value, 5);
        }
        finally { CoreSettings.Current.MasterVolume = volume; CoreAudio.PlayOneShotProvider = previous; }
    }

    private sealed class Drone : LayeredAudio.ILayerPlayer
    {
        public int Last;
        public int Volume { set => Last = value; }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(32)]
    [InlineData(100)]
    public Task MantraDroneAndTonesStartAtMasterVolume(int master) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreAudio.PlayOneShotProvider;
        var opener = MantraWindow.DroneOpener;
        var settings = CoreSettings.Current;
        var levels = (settings.MasterVolume, settings.MantraDroneVolume);
        var drone = new Drone();
        float? tone = null;
        settings.MasterVolume = master;
        settings.MantraDroneVolume = 30;
        MantraWindow.DroneOpener = _ => drone;
        CoreAudio.PlayOneShotProvider = (_, v, _, _, _) => tone = v;
        MantraWindow? window = null;
        try
        {
            window = new MantraWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            // port/7.2 DroneGain (#4080): level x drone dial x master from the first frame.
            Assert.Equal(ToneWav.VlcVolume(MantraWindow.DroneGain(0.05f, 30, master) * ToneWav.DronePeak), drone.Last);
            typeof(MantraWindow).GetMethod("PlayTone", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { 440.0, 100 });
            Assert.Equal(0.15f * master / 100f, tone!.Value, 5);
        }
        finally
        {
            window?.Close();
            CoreAudio.PlayOneShotProvider = previous;
            MantraWindow.DroneOpener = opener;
            (settings.MasterVolume, settings.MantraDroneVolume) = levels;
        }
        return Task.CompletedTask;
    });

    /// <summary>The idle drone follows master changes on the float tick (port/7.2 polls MasterVolume
    /// there), including a change made off the UI thread, and a stopped drone is never touched again.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IdleDroneFollowsVolumeChangesAndStaysStoppedAfterStop(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var opener = MantraWindow.DroneOpener;
        var settings = CoreSettings.Current;
        var levels = (settings.MasterVolume, settings.MantraDroneVolume);
        var drone = new Drone();
        MantraWindow.DroneOpener = _ => drone;
        var window = new MantraWindow();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Call(string method) => typeof(MantraWindow).GetMethod(method, flags)!.Invoke(window, null);
        void Tick() => typeof(MantraWindow).GetMethod("FloatTimer_Tick", flags)!.Invoke(window, new object?[] { null, EventArgs.Empty });
        try
        {
            settings.MasterVolume = 100;
            settings.MantraDroneVolume = 100;
            Call("StartDrone");
            Assert.True(drone.Last > 0);
            settings.MasterVolume = 0;
            Tick();
            Assert.Equal(0, drone.Last);
            settings.MasterVolume = 25;
            Tick();
            Assert.Equal(ToneWav.VlcVolume(MantraWindow.DroneGain(0.05f, 100, 25) * ToneWav.DronePeak), drone.Last);
            Task.Run(() => settings.MasterVolume = 0).GetAwaiter().GetResult();
            Tick();
            Assert.Equal(0, drone.Last);
            if (panic)
            {
                typeof(MantraWindow).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, window);
                MantraWindow.StopForPanic();
            }
            else Call("Cleanup");
            var stopped = drone.Last;
            settings.MasterVolume = 100;
            Tick();
            Assert.Equal(stopped, drone.Last);
        }
        finally
        {
            Call("Cleanup");
            window.Close();
            MantraWindow.DroneOpener = opener;
            (settings.MasterVolume, settings.MantraDroneVolume) = levels;
        }
        return Task.CompletedTask;
    });

}
