using System;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
public partial class DevicesSettingsSection
{
        private bool _webcamMonitorPopulating;
        /// <summary>WPF FillMonitorCombo: "Primary" first (survives a monitor reorder), then one row per screen.</summary>
        internal void RefreshWebcamMonitorList()
        {
            _webcamMonitorPopulating = true;
            try
            {
                var saved = CoreSettings.Current.WebcamCalibrationScreen ?? "Primary";
                CmbWebcamMonitor.Items.Clear();
                CmbWebcamMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("webcam_monitor_primary"), Tag = "Primary" });
                int target = 0;
                var all = TopLevel.GetTopLevel(this)?.Screens?.All;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var name = Platform.WebcamScreen.NameOf(all[i], i);
                        CmbWebcamMonitor.Items.Add(new ComboBoxItem
                        {
                            Content = string.Format(Loc.Get("webcam_monitor_item_fmt"), i + 1, name, all[i].Bounds.Width, all[i].Bounds.Height),
                            Tag = name,
                        });
                        if (string.Equals(name, saved, StringComparison.OrdinalIgnoreCase)) target = i + 1;
                    }
                CmbWebcamMonitor.SelectedIndex = target;
            }
            catch (Exception ex) { Log.Debug("Settings/Devices: monitor list failed: {Error}", ex.Message); }
            finally { _webcamMonitorPopulating = false; }
        }

        private void CmbWebcamMonitor_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbWebcamMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not string name) return;
            var s = CoreSettings.Current;
            if (string.Equals(s.WebcamCalibrationScreen, name, StringComparison.OrdinalIgnoreCase)) return;
            s.WebcamCalibrationScreen = name;
            CoreSettings.Save();
            AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
        }

}
