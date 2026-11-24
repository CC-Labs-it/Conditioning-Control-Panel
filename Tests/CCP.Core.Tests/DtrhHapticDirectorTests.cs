using System;
using System.Reflection;
using System.Threading;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The DtRH haptic director in Core (WPF Services/Haptics/DtrhHapticDirector.cs): the ambient curve,
/// the page feed and lifecycle it holds, and that a head with no device is a quiet no-op.
/// </summary>
public class DtrhHapticDirectorTests
{
    [Fact]
    public void TheAmbientFloorIsADepthGauge()
    {
        Assert.Equal(0, DtrhHapticDirector.AmbientTarget(0, 1, 1));               // slider at zero: off
        Assert.Equal(0.15, DtrhHapticDirector.AmbientTarget(0.5, 0, 0), 3);       // a whisper at the surface
        Assert.Equal(0.5, DtrhHapticDirector.AmbientTarget(0.5, 1, 0), 3);        // the slider's value at the bottom
        Assert.Equal(0.75, DtrhHapticDirector.AmbientTarget(0.5, 1, 1), 3);       // the melt pushes past it
        Assert.Equal(1.0, DtrhHapticDirector.AmbientTarget(1, 1, 1), 3);          // never over full
        Assert.Equal(0.06, DtrhHapticDirector.AmbientTarget(0.1, 0, 0), 3);       // clears the <= 0.05 = off cutoff
    }

    [Fact]
    public void WithNoDeviceEveryEntryIsAQuietNoOp()
    {
        var old = CoreHaptics.Service;
        CoreHaptics.Service = null;
        try
        {
            DtrhHapticDirector.OnLaunch(testMode: false);
            Assert.True(DtrhHapticDirector.Snapshot.Active);
            Assert.False(DtrhHapticDirector.IsReady);                             // nothing to drive

            DtrhHapticDirector.OnRunStarted();
            DtrhHapticDirector.OnHapticState(JObject.Parse("{\"running\":true,\"depth\":1.7,\"melt\":-2}"));
            var s = DtrhHapticDirector.Snapshot;
            Assert.True(s.RunActive);
            Assert.True(s.PageRunning);
            Assert.Equal(1, s.Depth);                                             // clamped 0..1
            Assert.Equal(0, s.Melt);

            DtrhHapticDirector.OnGameEvent(JObject.Parse("{\"event\":\"detonated\"}"));
            DtrhHapticDirector.OnGameEvent(JObject.Parse("{\"event\":\"effect-fired\",\"kind\":\"flash\"}"));
            DtrhHapticDirector.OnWorldFreeze(true);
            Assert.True(DtrhHapticDirector.Snapshot.WorldFrozen);
            DtrhHapticDirector.OnWorldFreeze(false);
            DtrhHapticDirector.OnVideoCovering(true);
            Assert.True(DtrhHapticDirector.Snapshot.VideoCovering);
            DtrhHapticDirector.OnVideoCovering(false);

            DtrhHapticDirector.OnRunEnded();
            s = DtrhHapticDirector.Snapshot;
            Assert.False(s.RunActive);
            Assert.False(s.PageRunning);
            Assert.Equal(0, s.Depth);
        }
        finally
        {
            DtrhHapticDirector.OnClosed();
            CoreHaptics.Service = old;
        }
        Assert.False(DtrhHapticDirector.Snapshot.Active);
        DtrhHapticDirector.OnClosed();                                            // twice is fine
    }

    /// <summary>The safety stops (WPF DtrhHapticDirector OnWorldFreeze / OnClosed / OnSettingsChanged):
    /// each one zeroes the Dtrh layer on a real HapticService, and only that layer.</summary>
    [Fact]
    public void FreezeCloseAndSwitchOffZeroTheDtrhLayer()
    {
        var settings = new AppSettings();
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetField("<Current>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, settings);
        var haptics = new HapticService(new HapticSettings());
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHaptics = CoreHaptics.Service;
        CoreSettings.ServiceProvider = () => service;
        CoreHaptics.Service = haptics;
        try
        {
            // The mixer's loop sees the gate closed (no premium here) and clears every layer on
            // its first tick. Stop the loop and wait for it to end, so it can never tick again and
            // a zero below is the director's doing, not the mixer's.
            var mixerType = typeof(HapticMixer);
            var loopCts = (CancellationTokenSource)mixerType.GetField("_loopCts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(haptics.Mixer)!;
            var loopTask = (System.Threading.Tasks.Task)mixerType.GetField("_loopTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(haptics.Mixer)!;
            loopCts.Cancel();
            Assert.True(((IAsyncResult)loopTask).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(10)));   // ended (canceled or not)
            haptics.SetLayer(HapticLayer.Manual, 1);
            Assert.Equal(1, haptics.GetLayer(HapticLayer.Manual));                // nothing clears it now

            DtrhHapticDirector.OnLaunch(testMode: false);

            haptics.SetLayer(HapticLayer.Dtrh, 0.5);
            haptics.SetLayer(HapticLayer.Video, 0.4);
            DtrhHapticDirector.OnWorldFreeze(true);
            Assert.Equal(0, haptics.GetLayer(HapticLayer.Dtrh));
            Assert.Equal(0.4, haptics.GetLayer(HapticLayer.Video), 3);                // ours only
            DtrhHapticDirector.OnWorldFreeze(false);

            haptics.SetLayer(HapticLayer.Dtrh, 0.5);
            settings.Haptics.Enabled = false;                                     // switched off mid-run
            Assert.Equal(0, haptics.GetLayer(HapticLayer.Dtrh));

            haptics.SetLayer(HapticLayer.Dtrh, 0.5);
            DtrhHapticDirector.OnClosed();
            Assert.Equal(0, haptics.GetLayer(HapticLayer.Dtrh));
        }
        finally
        {
            DtrhHapticDirector.OnClosed();
            CoreHaptics.Service = oldHaptics;
            CoreSettings.ServiceProvider = oldProvider;
            haptics.Dispose();
        }
    }

    /// <summary>Review P1 (s10-game-dtrh-1): a panic close that banks the run must leave nothing queued on
    /// the toy. The farewell wave is skipped when asked and cancelled by OnClosed, so it cannot start
    /// after the mixer's 400 ms panic mute (HapticMixer.PanicMuteMs) has run out.</summary>
    [Fact]
    public void CloseCancelsTheFarewellAndATeardownEndSkipsIt()
    {
        var settings = new AppSettings();
        settings.Haptics.Enabled = true;
        settings.Haptics.DtrhEnabled = true;
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetField("<Current>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, settings);
        var oldProvider = CoreSettings.ServiceProvider;
        var oldHaptics = CoreHaptics.Service;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        CoreEntitlement.HasPremiumProvider = () => true;   // before the mixer's first tick
        var haptics = new HapticService(settings.Haptics);
        haptics.DeviceManager.Register(new ConnectedProvider());
        CoreSettings.ServiceProvider = () => service;
        CoreHaptics.Service = haptics;
        var sequences = (System.Collections.ICollection)typeof(HapticMixer)
            .GetField("_sequences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(haptics.Mixer)!;
        int Live() => sequences.Count;   // a racy read is fine: the test polls
        bool WaitLive(int n) { var until = DateTime.UtcNow.AddSeconds(5); while (Live() != n && DateTime.UtcNow < until) Thread.Sleep(10); return Live() == n; }
        try
        {
            DtrhHapticDirector.OnLaunch(testMode: false);
            Assert.True(DtrhHapticDirector.IsReady);

            // Teardown end (panic watchdog, window closed mid-run): no wave at all.
            DtrhHapticDirector.OnRunStarted();
            var before = DtrhHapticDirector.FarewellTask;
            DtrhHapticDirector.OnRunEnded(farewell: false);
            Assert.Same(before, DtrhHapticDirector.FarewellTask);
            Assert.True(WaitLive(0));                                             // the run-started accent was cancelled too

            // An ordinary end plays the wave; the close that follows cuts it.
            DtrhHapticDirector.OnRunStarted();
            DtrhHapticDirector.OnRunEnded();
            var farewell = DtrhHapticDirector.FarewellTask!;
            Assert.True(WaitLive(1));                                             // the wave is on the mixer
            DtrhHapticDirector.OnClosed();
            Assert.Equal(0, Live());                                              // nothing left to play
            Assert.True(farewell.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            DtrhHapticDirector.OnClosed();
            CoreHaptics.Service = oldHaptics;
            CoreSettings.ServiceProvider = oldProvider;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            haptics.Dispose();
        }
    }

    private sealed class ConnectedProvider : IHapticProviderV2
    {
        public string Key => "test-connected";
        public string DisplayName => Key;
        public bool IsConnected => true;
        public System.Collections.Generic.IReadOnlyList<HapticDevice> Devices => Array.Empty<HapticDevice>();
#pragma warning disable CS0067
        public event EventHandler<HapticToyEvent>? ToyEvent;
        public event EventHandler? DevicesChanged;
        public event EventHandler<string>? Error;
#pragma warning restore CS0067
        public System.Threading.Tasks.Task<bool> ConnectAsync(CancellationToken ct) => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task DisconnectAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task SetOutputsAsync(string id, System.Collections.Generic.IReadOnlyList<ActuatorOutput> outputs, CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task StopAllAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<bool> PingAsync() => System.Threading.Tasks.Task.FromResult(true);
        public void Dispose() { }
    }
}
