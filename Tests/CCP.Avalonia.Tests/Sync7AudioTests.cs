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
    [InlineData(100)]
    public Task MantraDroneAndTonesStartAtMasterVolume(int master) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreAudio.PlayOneShotProvider;
        var opener = MantraWindow.DroneOpener;
        var volume = CoreSettings.Current.MasterVolume;
        var drone = new Drone();
        float? tone = null;
        CoreSettings.Current.MasterVolume = master;
        MantraWindow.DroneOpener = _ => drone;
        CoreAudio.PlayOneShotProvider = (_, v, _, _, _) => tone = v;
        MantraWindow? window = null;
        try
        {
            window = new MantraWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ToneWav.VlcVolume(0.05 * ToneWav.DronePeak * master / 100.0), drone.Last);
            typeof(MantraWindow).GetMethod("PlayTone", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { 440.0, 100 });
            Assert.Equal(0.15f * master / 100f, tone!.Value, 5);
        }
        finally
        {
            window?.Close();
            CoreAudio.PlayOneShotProvider = previous;
            MantraWindow.DroneOpener = opener;
            CoreSettings.Current.MasterVolume = volume;
        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IdleDroneFollowsVolumeChangesAndUnhooksOnStop(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var opener = MantraWindow.DroneOpener;
        var settings = CoreSettings.Current;
        var volume = settings.MasterVolume;
        var drone = new Drone();
        MantraWindow.DroneOpener = _ => drone;
        var window = new MantraWindow();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Call(string method) => typeof(MantraWindow).GetMethod(method, flags)!.Invoke(window, null);
        bool Subscribed() => (typeof(ConditioningControlPanel.Models.AppSettings)
            .GetField("PropertyChanged", flags)!.GetValue(settings) as Delegate)?
            .GetInvocationList().Any(d => ReferenceEquals(d.Target, window)) == true;
        try
        {
            settings.MasterVolume = 100;
            Call("StartDrone");
            Assert.True(drone.Last > 0);
            settings.MasterVolume = 0;
            Assert.Equal(0, drone.Last);
            settings.MasterVolume = 25;
            Assert.Equal(ToneWav.VlcVolume(0.05 * ToneWav.DronePeak * 0.25), drone.Last);
            Task.Run(() => settings.MasterVolume = 0).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, drone.Last);
            Assert.True(Subscribed());
            if (panic)
            {
                typeof(MantraWindow).GetField("_open", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, window);
                MantraWindow.StopForPanic();
            }
            else Call("Cleanup");
            Assert.False(Subscribed());
            var stopped = drone.Last;
            settings.MasterVolume = 100;
            Assert.Equal(stopped, drone.Last);
        }
        finally
        {
            Call("Cleanup");
            window.Close();
            MantraWindow.DroneOpener = opener;
            settings.MasterVolume = volume;
        }
        return Task.CompletedTask;
    });

}
