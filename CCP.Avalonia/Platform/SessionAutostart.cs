using System;
using System.IO;
using System.Runtime.Versioning;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Run on startup, per OS: the Windows Startup-folder shortcut (<see cref="WindowsStartupShortcut"/>,
    /// WPF StartupManager) on Windows, the freedesktop autostart entry (<see cref="XdgAutostart"/>) elsewhere.
    /// Windows never reads ~/.config/autostart, so routing both OSes through XdgAutostart did nothing there.
    /// </summary>
    internal static class SessionAutostart
    {
        internal static bool IsRegistered() =>
            OperatingSystem.IsWindows() ? WindowsStartupShortcut.IsRegistered() : XdgAutostart.IsRegistered();

        /// <summary>WPF StartupManager.SetStartupState: true when the OS now matches <paramref name="enabled"/>.</summary>
        internal static bool SetStartupState(bool enabled) =>
            OperatingSystem.IsWindows() ? WindowsStartupShortcut.SetStartupState(enabled) : XdgAutostart.SetStartupState(enabled);

        /// <summary>The file that stands for the registration on this OS.</summary>
        internal static string EntryPath =>
            OperatingSystem.IsWindows() ? WindowsStartupShortcut.ShortcutPath : XdgAutostart.EntryPath;
    }

    /// <summary>
    /// WPF StartupManager (ConditioningControlPanel/Services/StartupManager.cs) for this head: a
    /// shortcut in the user's Startup folder (AV-friendly, no Run key), launching the exe with
    /// "--startup". Same file name as WPF, so an entry WPF wrote is the one this head finds, adopts
    /// and removes. The installed exe keeps WPF's name and folder, so a WPF shortcut still targets it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WindowsStartupShortcut
    {
        internal const string ShortcutName = "ConditioningControlPanel.lnk";

        /// <summary>Test seam: the Startup folder. Null = a CCP_USERDATA_DIR sandbox's own folder,
        /// else the user's real Startup folder.</summary>
        internal static string? DirectoryOverride { get; set; }

        /// <summary>A sandboxed run (kc, tests, desk runs) never touches the real Startup folder,
        /// as LauncherShortcutWriter keeps its shortcuts in the sandbox.</summary>
        internal static string StartupFolder =>
            DirectoryOverride
            ?? (Environment.GetEnvironmentVariable("CCP_USERDATA_DIR") is { Length: > 0 } root
                ? Path.Combine(root, "shortcuts", "Startup")
                : Environment.GetFolderPath(Environment.SpecialFolder.Startup));

        internal static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

        internal static bool IsRegistered()
        {
            try { return File.Exists(ShortcutPath); }
            catch (Exception ex)
            {
                Log.Warning("Could not check startup registration: {Error}", ex.Message);
                return false;
            }
        }

        internal static bool SetStartupState(bool enabled)
        {
            var path = ShortcutPath;
            try
            {
                if (!enabled)
                {
                    if (File.Exists(path)) File.Delete(path);
                    Log.Information("Unregistered from Windows startup");
                    return true;
                }

                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    Log.Warning("Could not find executable path for startup registration");
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ShellLinkFile.Save(path, exe, Path.GetDirectoryName(exe) ?? "", "Conditioning Control Panel", "--startup");
                Log.Information("Registered for Windows startup: {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update Windows startup");
                return false;
            }
        }
    }
}
