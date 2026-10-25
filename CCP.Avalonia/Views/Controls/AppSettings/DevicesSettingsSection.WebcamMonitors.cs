using System;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;

public partial class DevicesSettingsSection
{
    private bool _monitorPopulating;
    private void PopulateWebcamMonitors()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || window.Screens is not { } screens) return;
        _monitorPopulating = true;
        try
        {
            CmbWebcamMonitor.Items.Clear();
            CmbWebcamMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("webcam_monitor_primary"), Tag = "Primary" });
            int n = 1, target = 0;
            foreach (var screen in screens.All)
            {
                CmbWebcamMonitor.Items.Add(new ComboBoxItem
                {
                    Content = string.Format(Loc.Get("webcam_monitor_item_fmt"), n++, screen.DisplayName, screen.Bounds.Width, screen.Bounds.Height),
                    Tag = screen.DisplayName
                });
                if (string.Equals(screen.DisplayName, CoreSettings.Current.WebcamCalibrationScreen, StringComparison.OrdinalIgnoreCase))
                    target = CmbWebcamMonitor.Items.Count - 1;
            }
            CmbWebcamMonitor.SelectedIndex = target;
        }
        finally { _monitorPopulating = false; }
    }

    private void WebcamMonitorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_monitorPopulating || CmbWebcamMonitor.SelectedItem is not ComboBoxItem { Tag: string name } item) return;
        if (string.Equals(CoreSettings.Current.WebcamCalibrationScreen, name, StringComparison.OrdinalIgnoreCase)) return;
        CoreSettings.Current.WebcamCalibrationScreen = name;
        CoreSettings.Save();
        AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
    }
}
