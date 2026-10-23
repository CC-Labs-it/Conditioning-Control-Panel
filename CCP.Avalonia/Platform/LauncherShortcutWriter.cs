using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF 7.1.5 Services/Launcher/LauncherShortcuts.cs:69 TryCreateDesktopShortcut, this head's half:
    /// a .lnk on the desktop on Windows (StartupManager.cs:134 COM writer), a freedesktop desktop entry
    /// on Linux, in the app menu (<c>$XDG_DATA_HOME/applications</c>) and on the desktop folder when the
    /// session has one. Names and arguments are Core's <see cref="LauncherShortcuts"/>.
    /// </summary>
    internal static class LauncherShortcutWriter
    {
        /// <summary>Tests: the desktop and the Linux applications folder. Null = the OS answer.</summary>
        internal static string? DesktopOverride { get; set; }
        internal static string? ApplicationsOverride { get; set; }

        /// <summary>True when the shortcut exists afterwards, including when it was already there;
        /// false (logged, never thrown) on an unknown id or any writer failure.</summary>
        internal static bool TryCreateDesktopShortcut(string? gameId)
        {
            try
            {
                string? title = null;
                if (!string.IsNullOrWhiteSpace(gameId) &&
                    !string.Equals(gameId.Trim(), LauncherShortcuts.PanelId, StringComparison.OrdinalIgnoreCase))
                {
                    var card = LauncherCards.Find(gameId);
                    if (card == null) { Log.Warning("[Launcher] no shortcut for unknown game {Id}", gameId); return false; }
                    gameId = card.Id;
                    title = Loc.Get(card.TitleKey);
                }

                // A test host is not the app: its shortcut would be a dead icon on a real desktop. Run as
                // `dotnet testhost.dll` the process is dotnet, so the entry assembly decides.
                var host = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "";
                if (DesktopOverride == null && ApplicationsOverride == null &&
                    (host.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)))
                    return false;
                // The icons ship beside the app (csproj launcher-icons), whatever process hosts it.
                var (exe, dir) = (Environment.ProcessPath ?? "", AppContext.BaseDirectory);
                var icon = LauncherShortcuts.ResolveIcon(gameId, dir);
                return OperatingSystem.IsWindows() ? WriteLnk(gameId, title, exe, dir, icon) : WriteDesktopEntry(gameId, title, icon);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Launcher] shortcut for {Id} failed", gameId);
                return false;
            }
        }

        /// <summary>A sandboxed run (CCP_USERDATA_DIR) keeps its shortcuts inside the sandbox.</summary>
        private static string? Sandbox(string leaf) =>
            Environment.GetEnvironmentVariable("CCP_USERDATA_DIR") is { Length: > 0 } root
                ? Path.Combine(root, "shortcuts", leaf) : null;

        private static string DesktopFolder() => DesktopOverride ?? Sandbox("Desktop")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static bool WriteDesktopEntry(string? gameId, string? title, string? icon)
        {
            var (fileName, content) = LauncherShortcuts.DescribeDesktopEntry(gameId, title, XdgAutostart.ExecLine(), icon);
            var apps = ApplicationsOverride ?? Sandbox("applications") ?? Path.Combine(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "applications");
            var folders = new List<string> { apps };
            // A session with no desktop folder (a tiling window manager) still gets the menu entry.
            var desktop = DesktopFolder();
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) folders.Add(desktop);

            bool any = false;
            foreach (var folder in folders)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, fileName);
                    // Rewritten, not skipped, as WPF: an old entry picks up a new icon or exe path.
                    File.WriteAllText(path, content);
                    // A desktop entry on the desktop must be executable or the shell will not launch it.
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
                    Log.Information("[Launcher] desktop entry written: {Path}", path);
                    any = true;
                }
                catch (Exception ex) { Log.Warning(ex, "[Launcher] desktop entry in {Folder} failed", folder); }
            }
            return any;
        }

        [SupportedOSPlatform("windows")]
        private static bool WriteLnk(string? gameId, string? title, string exe, string dir, string? icon)
        {
            var (fileName, arguments) = LauncherShortcuts.Describe(gameId, title);
            var desktop = DesktopFolder();
            if (string.IsNullOrEmpty(desktop)) { Log.Warning("[Launcher] no desktop folder"); return false; }
            var path = Path.Combine(desktop, fileName);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Warning("[Launcher] executable path not found for the shortcut");
                return File.Exists(path);
            }

            // WPF StartupManager.CreateShortcut, line for line (Platform/ShellLinkFile).
            ShellLinkFile.Save(path, exe, dir, Path.GetFileNameWithoutExtension(fileName), arguments, icon);
            Log.Information("[Launcher] shortcut written: {Path} {Args}", path, arguments);
            return true;
        }
    }
}
