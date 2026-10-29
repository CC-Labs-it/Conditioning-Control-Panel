using System;
using System.IO;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The folder picked on the installer's Content Folder page. installer.iss (CurStepChanged,
    /// ssPostInstall) writes it to HKCU\Software\CodeBambi\Conditioning Control Panel, value
    /// AssetsPath; WPF App.xaml.cs ApplyInstallerAssetsPath reads it once at start-up, adopts it as
    /// CustomAssetsPath unless the user already set one, then deletes the value. Same rule here,
    /// Windows only (no installer elsewhere). Without it content landed in AppData.
    /// </summary>
    internal static class InstallerAssetsPath
    {
        /// <summary>installer.iss: 'Software\{#MyAppPublisher}\{#MyAppName}' (pinned by InstallerAssetsPathTests).</summary>
        internal const string SubKey = @"Software\CodeBambi\Conditioning Control Panel";
        internal const string ValueName = "AssetsPath";

        /// <summary>
        /// WPF's rule: the installer's folder becomes CustomAssetsPath only when it exists, differs from
        /// the default folder and the user has no custom path yet. Null = change nothing.
        /// </summary>
        internal static string? PathToAdopt(string? installerPath, string defaultPath, string? currentCustom, Func<string, bool> exists)
        {
            if (string.IsNullOrWhiteSpace(installerPath)) return null;
            if (!exists(installerPath)) return null;
            if (string.Equals(installerPath, defaultPath, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrWhiteSpace(currentCustom)) return null;
            return installerPath;
        }

        /// <summary>Start-up, once settings are loaded and before anything resolves the assets folder.</summary>
        internal static void Apply(SettingsService? settings)
        {
            if (settings?.Current == null) return;
            Apply(SubKey, Path.Combine(CorePaths.UserData, "assets"), () => settings.Current.CustomAssetsPath, path =>
            {
                settings.Current.CustomAssetsPath = path;
                settings.Save();
            });
        }

        /// <summary>The registry half, on any HKCU subkey (tests use a throwaway one).</summary>
        internal static void Apply(string subKey, string defaultPath, Func<string?> currentCustom, Action<string> adoptPath)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, writable: true);
                if (key == null) return;
                if (key.GetValue(ValueName) is not string installerPath || string.IsNullOrWhiteSpace(installerPath)) return;

                var adopt = PathToAdopt(installerPath, defaultPath, currentCustom(), Directory.Exists);
                if (adopt != null)
                {
                    adoptPath(adopt);
                    Log.Information("Applied installer assets path: {Path}", adopt);
                }

                // One-time, as WPF: the value goes once read, so a later settings change is never undone.
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Debug("Cleared installer AssetsPath registry value");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply installer assets path from registry");
            }
        }
    }
}
