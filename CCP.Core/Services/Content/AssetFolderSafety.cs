using System;
using System.IO;

namespace ConditioningControlPanel.Services;

/// <summary>Shared #1053 content-folder rule from WPF SecurityHelper. Exact roots are refused;
/// a dedicated child remains usable. Resolving an invalid path fails closed.</summary>
public static class AssetFolderSafety
{
    public static bool IsPersonalFolderRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return true;

        try
        {
            var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

            // A bare drive root ("D:\") trims to "D:" - and Path.GetPathRoot of any path
            // under it returns "D:\", so compare the trimmed forms.
            var root = Path.GetPathRoot(dir);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(dir, Path.TrimEndingDirectorySeparator(root!), StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var folder in new[]
            {
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.CommonDocuments,
                Environment.SpecialFolder.CommonPictures,
                Environment.SpecialFolder.CommonVideos,
            })
            {
                var known = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(known)) continue;
                if (string.Equals(dir, Path.TrimEndingDirectorySeparator(Path.GetFullPath(known)),
                                  StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Downloads and OneDrive have no SpecialFolder id. Downloads is a known folder
            // the user can relocate, but the profile-relative name catches the default, and
            // a relocated Downloads that is not one of the roots above is rare enough to
            // leave to the exact-match rule.
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                foreach (var name in new[] { "Downloads", "Desktop", "Documents", "Pictures", "Videos", "Music" })
                {
                    if (string.Equals(dir, Path.Combine(profile, name), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var oneDrive = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(oneDrive)) continue;
                var oneDriveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oneDrive));
                if (string.Equals(dir, oneDriveRoot, StringComparison.OrdinalIgnoreCase)) return true;
                // OneDrive's Known Folder Move relocates Desktop/Documents/Pictures under it.
                foreach (var name in new[] { "Desktop", "Documents", "Pictures", "Videos", "Music" })
                {
                    if (string.Equals(dir, Path.Combine(oneDriveRoot, name), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            // A path we cannot even resolve is not a path we should scan.
            Serilog.Log.Warning(ex, "IsPersonalFolderRoot check failed for: {Path}", directory);
            return true;
        }
    }
}
