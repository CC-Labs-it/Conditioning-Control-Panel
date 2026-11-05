// WPF MainWindow.StartStop.cs:258-310: local-library warnings and both recovery actions.
using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
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
