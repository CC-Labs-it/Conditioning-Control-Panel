using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The files a factory reset must remove next to the moved-aside settings.json, because each one
    /// hands old settings back on the next launch.
    ///
    /// <para><c>settings.json*.tmp</c>: <c>SettingsService.RecoverAndCleanTempFiles</c> promotes a
    /// surviving temp to settings.json when the real file is missing. <c>settings.json.restored</c>:
    /// the cloud-restore marker. <see cref="SessionSettingsJournal.FileName"/> and
    /// <see cref="SessionSettingsJournal.RampFileName"/>: a reset during a session or an intensity
    /// ramp left them behind, and <c>SessionSettingsJournal.HealOnLaunch</c> then wrote the user's
    /// pre-session values over the fresh defaults (2026-10-22).</para>
    /// </summary>
    internal static class FactoryResetSweep
    {
        /// <summary>How long the detached relaunch waits. Longer than <see cref="Diagnostics.UiThreadExit.GraceMs"/>:
        /// an old process still inside its exit window would ack the new one's show signal and the new
        /// one would quit, leaving no app at all.</summary>
        internal const int RelaunchDelaySeconds = Diagnostics.UiThreadExit.GraceMs / 1000 + 3;

        /// <summary>The leftovers present in <paramref name="dir"/> right now. Never throws.</summary>
        internal static IReadOnlyList<string> Leftovers(string dir)
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return found;
                found.AddRange(Directory.GetFiles(dir, "settings.json*.tmp"));
                foreach (var name in new[] { "settings.json.restored", SessionSettingsJournal.FileName, SessionSettingsJournal.RampFileName })
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path)) found.Add(path);
                }
            }
            catch { }
            return found;
        }

        /// <summary>Deletes every leftover. Returns the ones it could not delete. Never throws.</summary>
        internal static IReadOnlyList<string> Delete(string dir, Action<string, string?>? log = null)
        {
            var failed = new List<string>();
            foreach (var path in Leftovers(dir))
            {
                try { File.Delete(path); log?.Invoke(path, null); }
                catch (Exception ex) { failed.Add(path); log?.Invoke(path, ex.Message); }
            }
            return failed;
        }
    }
}
