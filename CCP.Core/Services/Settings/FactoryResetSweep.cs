using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The files a factory reset must remove next to the moved-aside settings.json, because each one
    /// hands old settings back on the next launch (main baf86f0ac + b2317b818; both heads' Danger Zone).
    ///
    /// <para><c>settings.json*.tmp</c>: <c>SettingsService.RecoverAndCleanTempFiles</c> promotes a
    /// surviving temp to settings.json when the real file is missing. <c>settings.json.restored</c>:
    /// the cloud-restore marker. <see cref="SessionJournalFileName"/> / <see cref="RampJournalFileName"/>
    /// (SessionSettingsJournal.FileName / RampFileName): a reset during a session or an intensity ramp
    /// left them behind, and the journal's launch heal then wrote the pre-session values over the fresh
    /// defaults (2026-10-22). <see cref="Remote.RemoteHoldings.FileName"/> and <c>lockdown_recovery.json</c>:
    /// RemoteHoldings.HealOnLaunch and LockdownService.RecoverIfNeeded do the same.</para>
    /// </summary>
    internal static class FactoryResetSweep
    {
        // The SessionSettingsJournal file names (main 36370021a / 8a89eaeb0). Literal here so this rule does
        // not wait on that port; SessionSettingsJournalTests pin the journal's own constants to the same names.
        internal const string SessionJournalFileName = "session_restore.json";
        internal const string RampJournalFileName = "ramp_restore.json";
        internal const string LockdownRecoveryFileName = "lockdown_recovery.json";

        /// <summary>The longest a head's exit may take (WPF UiThreadExit.GraceMs, main 4dcdb625f).</summary>
        internal const int ExitGraceMs = 5000;

        /// <summary>How long the detached relaunch waits. Longer than the exit window: an old process still
        /// inside it would ack the new one's show signal and the new one would quit, leaving no app at all.</summary>
        internal const int RelaunchDelaySeconds = ExitGraceMs / 1000 + 3;

        /// <summary>The leftovers present in <paramref name="dir"/> right now. Never throws.</summary>
        internal static IReadOnlyList<string> Leftovers(string dir)
        {
            var found = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return found;
                found.AddRange(Directory.GetFiles(dir, "settings.json*.tmp"));
                foreach (var name in new[] { "settings.json.restored", SessionJournalFileName, RampJournalFileName,
                             Remote.RemoteHoldings.FileName, LockdownRecoveryFileName })
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
