using System;
using System.ComponentModel;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

// ============================ toy pulses + live settings ============================
//
// Toys follow the Just Drop host (JustDropHapticDirector, 7.3.8): the page sends one clamped pulse
// (TcFrameGate: level 0..1, at most 3 s), the host only decides whether the toy takes it. It is a
// mixer transient, so the user's master intensity and cap apply; the Back Room's limiter holds the
// rate; the newest accepted pulse replaces the one in play. Nothing plays when HapticsAccess is shut,
// the master switch is off or no toy is connected, and the pulse stops the moment the window suspends
// (tier card), a toy leaves, the window closes or panic runs.
//
// Live settings: gentle, motion and the picture consent go to the page as `setting` frames while the
// window is up. The settings instance is followed across a cloud restore or a reset (CurrentReplaced).
internal static partial class TcHostService
{
    private static readonly object PulseGate = new();
    private static readonly BackRoomHapticDirector.Limiter PulseLimit = new();
    private static CancellationTokenSource? _pulse;
    private static AppSettings? _hookedSettings;
    private static bool _replaceHooked;

    /// <summary>A toy can be driven right now (what <c>caps.toysConnected</c> reports).</summary>
    private static bool ToysReady
        => HapticsAccess.IsOpen
        && App.Settings?.Current?.Haptics is { Enabled: true }
        && App.Haptics is { IsConnected: true };

    private static void OnHaptic(TcPageFrame f)
    {
        var haptics = App.Haptics;
        if (haptics == null || !ToysReady || _host == null || !TcFeelRules.Plays(f.Level, f.Ms)) return;
        CancellationTokenSource cts;
        lock (PulseGate)
        {
            if (!PulseLimit.Accept(Environment.TickCount64)) return;
            try { _pulse?.Cancel(); } catch { }
            cts = _pulse = new CancellationTokenSource();
        }
        var ms = TcFeelRules.DurationFor(f.Ms, haptics.IsButtplugProvider);
        _ = PlayPulseAsync(haptics, f.Level, ms, cts);
    }

    private static async System.Threading.Tasks.Task PlayPulseAsync(HapticService haptics, double level, int ms, CancellationTokenSource cts)
    {
        try { await haptics.PlayPatternAsync(level, ms, VibrationMode.Constant, BackRoomHapticDirector.PriorityFor(level), Haptics.Core.ToyRole.All, cts.Token); }
        catch { /* a toy that dropped mid-pulse is not the game's problem */ }
        finally
        {
            lock (PulseGate) { if (_pulse == cts) _pulse = null; }
            cts.Dispose();
        }
    }

    /// <summary>Stop OUR pulse only; whatever else drives the toy keeps going.</summary>
    private static void StopPulse()
    {
        CancellationTokenSource? cts;
        lock (PulseGate) { cts = _pulse; _pulse = null; }
        try { cts?.Cancel(); } catch { }
    }

    private static void OnToyConnectionChanged(object? sender, bool connected)
    {
        if (!connected) StopPulse();
    }

    /// <summary>Launch: follow the toy and the settings. Close: let go of both (<paramref name="on"/> false).</summary>
    private static void WatchFeel(bool on)
    {
        try
        {
            if (App.Haptics != null)
            {
                App.Haptics.ConnectionChanged -= OnToyConnectionChanged;
                if (on) App.Haptics.ConnectionChanged += OnToyConnectionChanged;
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        try
        {
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnAppSettingChanged;
            _hookedSettings = null;
            if (App.Settings != null && _replaceHooked) App.Settings.CurrentReplaced -= OnSettingsReplaced;
            _replaceHooked = false;
            if (!on) return;
            var s = App.Settings?.Current;
            if (s != null) { s.PropertyChanged += OnAppSettingChanged; _hookedSettings = s; }
            if (App.Settings != null) { App.Settings.CurrentReplaced += OnSettingsReplaced; _replaceHooked = true; }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private static void OnSettingsReplaced() => RunOnUi(() =>
    {
        if (_host == null) return;
        WatchFeel(true);
        foreach (var key in new[] { TcFeelRules.KeyGentle, TcFeelRules.KeyMotion, TcFeelRules.KeyConsent }) PushSetting(key);
    });

    private static void OnAppSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        var key = TcFeelRules.KeyFor(e.PropertyName);
        if (key != null) RunOnUi(() => PushSetting(key));
    }

    /// <summary>Send the app's current value for one key. The consent key also starts or stops the pictures.</summary>
    private static void PushSetting(string key)
    {
        if (_host == null) return;
        try
        {
            var s = App.Settings?.Current;
            JToken value;
            if (key == TcFeelRules.KeyGentle) value = Safety.GentleVisuals.On;
            else if (key == TcFeelRules.KeyMotion) value = TcHostRules.MotionWire(s?.MotionLevel ?? MotionLevel.Full);
            else
            {
                bool on = PicturesOnNow;
                if (on) StartPictures(); else StopPictures();
                value = on;
            }
            _host.Post(TcFeelRules.SettingFrame(key, value));
        }
        catch (Exception ex) { App.Logger?.Debug("Tc: setting {Key} push failed: {E}", key, ex.Message); }
    }
}
