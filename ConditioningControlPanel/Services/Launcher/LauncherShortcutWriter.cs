using System;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// Desktop shortcuts for the launcher's tiles. A game shortcut carries <c>--game &lt;id&gt;</c>
/// so the boot decision lands in the game; the panel shortcut carries <c>--panel</c>. Same COM
/// writer as the startup entry (<see cref="StartupManager"/>), same exe path rules.
/// The names, arguments and icons are Core's <see cref="LauncherShortcuts"/>.
/// </summary>
public static class LauncherShortcutWriter
{
    /// <summary>
    /// Writes the shortcut to the desktop. True when it exists afterwards, including when it was
    /// already there; false (logged, never thrown) on an unknown id or any writer failure.
    /// </summary>
    public static bool TryCreateDesktopShortcut(string? gameId)
    {
        try
        {
            string? title = null;
            if (!string.IsNullOrWhiteSpace(gameId) &&
                !string.Equals(gameId.Trim(), LauncherCatalogue.PanelId, StringComparison.OrdinalIgnoreCase))
            {
                var entry = LauncherCatalogue.Find(gameId);
                if (entry == null)
                {
                    Log.Warning("[Launcher] no shortcut for unknown game {Id}", gameId);
                    return false;
                }
                gameId = entry.Id;
                title = entry.Title;
            }

            var (fileName, arguments) = LauncherShortcuts.Describe(gameId, title);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) { Log.Warning("[Launcher] no desktop folder"); return false; }
            var path = Path.Combine(desktop, fileName);

            var exe = StartupManager.GetExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Warning("[Launcher] executable path not found for the shortcut");
                return File.Exists(path);
            }

            // An existing shortcut is rewritten, not skipped: the ones made before the icons
            // shipped get their picture the next time the player presses the button.
            var dir = Path.GetDirectoryName(exe) ?? "";
            StartupManager.CreateShortcut(path, exe, dir,
                Path.GetFileNameWithoutExtension(fileName), arguments, LauncherShortcuts.ResolveIcon(gameId, dir));
            Log.Information("[Launcher] shortcut written: {Path} {Args}", path, arguments);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Launcher] shortcut for {Id} failed", gameId);
            return false;
        }
    }
}
