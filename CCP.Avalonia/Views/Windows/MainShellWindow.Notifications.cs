// WPF MainWindow.StartStop.cs empty-library notices, MainWindow.xaml.cs OnLevelUp /
// legacy calibration prompt, and MainWindow.Companion.cs OnCompanionLevelUp.
using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private void InitializeShellNotifications()
    {
        Action<int> level = n => Dispatcher.UIThread.Post(() =>
        {
            if (!_notificationsClosed) NotifyPlayerLevel(n);
        });
        Action<CompanionId, int> companion = (id, n) => Dispatcher.UIThread.Post(() =>
        {
            if (!_notificationsClosed) NotifyCompanionLevel(id, n);
        });
        ProgressionBank.LevelUp += level;
        CompanionCore.LevelUp += companion;
        Opened += (_, _) => CheckLegacyCalibration();
        Closed += (_, _) =>
        {
            _notificationsClosed = true;
            ProgressionBank.LevelUp -= level;
            CompanionCore.LevelUp -= companion;
        };
    }

    private bool _notificationsClosed;

    internal static void NotifyPlayerLevel(int level) => OsNotifications.Show(
        Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", level));

    internal static void NotifyCompanionLevel(CompanionId id, int level)
    {
        var name = CoreMods.MakeModAware(CompanionDefinition.GetById(id).Name);
        if (level == CompanionProgress.MaxLevel)
            OsNotifications.Show("MAX LEVEL!", $"{name} has reached maximum level!");
        else if (level % 10 == 0)
            OsNotifications.Show("Companion Level Up!", $"{name} reached Level {level}!");
    }

    internal void CheckLegacyCalibration()
    {
        var bounds = WebcamTracker.Instance.Calibration?.MonitorBounds;
        if (bounds != null && string.IsNullOrEmpty(bounds.DeviceName))
            App.Notifications.ShowSticky("recalibrate-multimonitor",
                "Your calibration needs updating for multi-monitor support.", NotificationType.Warning,
                "Recalibrate", async () =>
                {
                    try { await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this); }
                    catch (Exception ex) { Log.Warning(ex, "Recalibrate toast: failed to open calibration window"); }
                });
    }

    internal void WarnIfFlashLibraryEmpty() => WarnIfLibraryEmpty(false);
    internal void WarnIfWallpaperLibraryEmpty() => WarnIfLibraryEmpty(true);

    private void WarnIfLibraryEmpty(bool wallpaper)
    {
        var s = CoreSettings.Current;
        if ((!wallpaper && !s.FlashEnabled) || !string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return;
        var folder = wallpaper && !string.IsNullOrWhiteSpace(s.WallpaperSourceFolder) && Directory.Exists(s.WallpaperSourceFolder)
            ? s.WallpaperSourceFolder : Path.Combine(CorePaths.EffectiveAssets, wallpaper ? "wallpapers" : "images");
        if (NotificationLibrary.HasMedia(folder, wallpaper)) return;
        App.Notifications.Show(string.Format(Loc.Get(wallpaper ? "msg_no_wallpaper_images" : "msg_no_flash_images"),
                Loc.Get("nav_door_library"), Loc.Get("tab_assets")), NotificationType.Warning, TimeSpan.FromSeconds(18),
            Loc.Get("btn_open_media_manager"), () => ShowTab("assets"),
            global::ConditioningControlPanel.Avalonia.Controls.IconText.BareLoc("btn_get_packs"), () =>
            {
                try { ExternalOpener.Open(DiscordLinks.PackCatalogue); }
                catch (Exception ex) { Log.Debug(ex, "Pack catalogue link failed"); }
            });
    }
}
