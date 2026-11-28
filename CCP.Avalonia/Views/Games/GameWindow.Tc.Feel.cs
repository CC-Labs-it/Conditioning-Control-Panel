// PORTED from ConditioningControlPanel/Services/Tc/TcHostService.Feel.cs (main 47f083c282): T&C toy pulses and live settings.
// Pulses go through the one shared pulse director (Core BackRoomHapticDirector, station "tc"), as Breakout does: behind
// HapticsAccess, the master switch and a connected toy, rate limited, the newest replaces the one in play, a mixer
// transient. Our pulse stops on close (panic, safe word, the second Escape, the X all close the window), the tier card's
// suspend and a toy leaving. Live settings: gentle and motion go to the page as setting frames while the window is up;
// the settings instance is followed across a restore and let go on close. The picture consent key waits for the
// pictures (s20-tc-media): this head offers no pictures, so there is nothing to start or stop.
using System;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>The shared pulse seam (tests swap it); a level of 0 is the stop.</summary>
        internal Action<BackRoomHaptic> TcHaptic = BackRoomHapticDirector.OnHaptic;

        private HapticService? _tcToys;
        private AppSettings? _tcSettings;
        private SettingsService? _tcSettingsService;

        /// <summary>WPF ToysReady (TcHostService.Feel.cs:30): what <c>caps.toysConnected</c> reports.</summary>
        private static bool TcToysReady => HapticsAccess.IsOpen
            && CoreSettings.Current.Haptics is { Enabled: true } && CoreHaptics.Service is { IsConnected: true };

        /// <summary>WPF OnHaptic (:35): only a real pulse, only while the window is up.</summary>
        private void OnTcHaptic(TcPageFrame f)
        {
            if (!_tcOpen || _closing || !HapticsAccess.IsOpen || !TcFeelRules.Plays(f.Level, f.Ms)) return;
            TcHaptic(new(TcFeelRules.Station, f.Level, f.Ms, "page"));
        }

        /// <summary>WPF StopPulse (:62): our pulse only, at once.</summary>
        private void StopTcPulse()
        {
            try { TcHaptic(new(TcFeelRules.Station, 0, 0, "host")); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnTcToyConnectionChanged(object? sender, bool connected)
        {
            if (!connected) StopTcPulse();
        }

        /// <summary>WPF WatchFeel (:75): follow the toy and the settings on launch, let go of both on close.</summary>
        private void WatchTcFeel(bool on)
        {
            try
            {
                if (_tcToys != null) _tcToys.ConnectionChanged -= OnTcToyConnectionChanged;
                _tcToys = on ? CoreHaptics.Service : null;
                if (_tcToys != null) _tcToys.ConnectionChanged += OnTcToyConnectionChanged;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            try
            {
                if (_tcSettings != null) _tcSettings.PropertyChanged -= OnTcSettingChanged;
                if (_tcSettingsService != null) _tcSettingsService.CurrentReplaced -= OnTcSettingsReplaced;
                _tcSettings = null;
                _tcSettingsService = null;
                if (!on) return;
                _tcSettings = CoreSettings.Current;
                _tcSettings.PropertyChanged += OnTcSettingChanged;
                _tcSettingsService = CoreSettings.Service;
                if (_tcSettingsService != null) _tcSettingsService.CurrentReplaced += OnTcSettingsReplaced;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>Test view: the settings instance this window follows (null once closed).</summary>
        internal AppSettings? TcWatchedSettings => _tcSettings;

        private void OnTcSettingsReplaced() => OnTcUi(() =>
        {
            if (!_tcOpen || _closing) return;
            WatchTcFeel(true);
            PushTcSetting(TcFeelRules.KeyGentle);
            PushTcSetting(TcFeelRules.KeyMotion);
        });

        private void OnTcSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            var key = TcFeelRules.KeyFor(e.PropertyName);
            if (key != null) OnTcUi(() => PushTcSetting(key));
        }

        private static void OnTcUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Post(a);
        }

        /// <summary>WPF PushSetting (:114): the app's current value for one key.</summary>
        private void PushTcSetting(string key)
        {
            if (!_tcOpen || _closing) return;
            try
            {
                JToken value;
                if (key == TcFeelRules.KeyGentle) value = GentleVisuals.On;
                else if (key == TcFeelRules.KeyMotion) value = TcHostRules.MotionWire(CoreSettings.Current.MotionLevel);
                else return;   // picture consent: s20-tc-media (no pictures on this head)
                Post(TcFeelRules.SettingFrame(key, value));
            }
            catch (Exception ex) { Log.Debug("Tc: setting {Key} push failed: {E}", key, ex.Message); }
        }
    }
}
