using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF StartStop empty-library notices: probe local folders, never online/mixed pools.
/// A filesystem failure means do not nag; finding one matching extension is enough.</summary>
public static class LocalMediaLibrary
{
    private static readonly string[] FlashExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    private static readonly string[] WallpaperExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

    public static bool FlashIsEmpty(AppSettings? settings, string assets) =>
        settings is { FlashEnabled: true, AudioOnlySession: false } && IsLocal(settings)
        && !FolderHasAnyMedia(Path.Combine(assets, "images"), FlashExtensions);

    public static bool WallpaperIsEmpty(AppSettings? settings, string assets)
    {
        if (!IsLocal(settings)) return false;
        var custom = settings!.WallpaperSourceFolder;
        var folder = !string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)
            ? custom : Path.Combine(assets, "wallpapers");
        return !FolderHasAnyMedia(folder, WallpaperExtensions);
    }

    private static bool IsLocal(AppSettings? settings) =>
        settings != null && string.Equals(settings.MediaSource, "local", StringComparison.OrdinalIgnoreCase);

    internal static bool FolderHasAnyMedia(string folder, string[] extensions,
        Func<string, IEnumerable<string>>? enumerate = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
            enumerate ??= path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
            foreach (var file in enumerate(folder))
                foreach (var wanted in extensions)
                    if (string.Equals(Path.GetExtension(file), wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return true; }
    }
}
