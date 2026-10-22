using System;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The head-independent rules of the native-bundle self-repair (WPF NativeBundleGuard): where
    /// the single-file host extracts natives, the one-attempt-per-version cooldown stamp and the
    /// batch helper that clears the cache after we exit. The head keeps the Skia probe, the
    /// process launch and the give-up dialog. See NativeBundleGuard for why the delete has to run
    /// from outside the process.
    /// </summary>
    public static class NativeBundleRepair
    {
        /// <summary>
        /// One repair attempt per version per window. A second failure inside it means the cache
        /// was not the problem (or the delete could not land), and looping would be worse than
        /// stopping: relaunch, fail, relaunch is indistinguishable from the app refusing to open.
        /// </summary>
        public static readonly TimeSpan RepairCooldown = TimeSpan.FromMinutes(10);

        private const string StampFileName = "native-repair.stamp";

        private static string StampPath(string userData) => Path.Combine(userData, StampFileName);

        /// <summary>
        /// The <c>&lt;base&gt;\&lt;app-name&gt;</c> folder holding every bundle-id the host has
        /// unpacked for <paramref name="exePath"/>, or null when this build does not self-extract
        /// (the entry assembly has a real location) or the folder is not there. Older versions'
        /// folders are swept with ours - they are pure cache and the host rebuilds whichever it needs.
        /// </summary>
        /// <param name="entryAssemblyLocation">The head's entry-assembly Location; a single-file bundle reports "".</param>
        /// <param name="extractBaseDir">DOTNET_BUNDLE_EXTRACT_BASE_DIR, which wins over the temp folder exactly as in the host.</param>
        public static string? ExtractionRoot(string? entryAssemblyLocation, string? exePath,
            string? extractBaseDir, string tempPath)
        {
            if (!string.IsNullOrEmpty(entryAssemblyLocation) || string.IsNullOrEmpty(exePath))
                return null;

            var baseDir = string.IsNullOrWhiteSpace(extractBaseDir) ? Path.Combine(tempPath, ".net") : extractBaseDir;
            var root = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(exePath));
            return Directory.Exists(root) ? root : null;
        }

        /// <summary>
        /// True when <paramref name="version"/> already burned its repair attempt inside the
        /// cooldown. A version bump gets a fresh attempt: the new bundle has its own extraction
        /// folder and deserves one. An unreadable stamp never blocks the repair - that would turn
        /// a transient IO hiccup into a permanently unstartable app.
        /// </summary>
        public static bool RepairedRecently(string userData, string version, DateTime utcNow)
        {
            try
            {
                var path = StampPath(userData);
                if (!File.Exists(path))
                    return false;

                var parts = File.ReadAllText(path).Trim().Split('|');
                if (parts.Length != 2)
                    return false;
                if (!string.Equals(parts[0], version, StringComparison.Ordinal))
                    return false;
                if (!long.TryParse(parts[1], out var ticks))
                    return false;

                return utcNow - new DateTime(ticks, DateTimeKind.Utc) < RepairCooldown;
            }
            catch
            {
                return false;
            }
        }

        public static void WriteStamp(string userData, string version, DateTime utcNow)
        {
            try
            {
                Directory.CreateDirectory(userData);
                File.WriteAllText(StampPath(userData), $"{version}|{utcNow.Ticks}");
            }
            catch { }
        }

        /// <summary>Healthy launch: drop the stamp so a future truncation gets its own full attempt.</summary>
        public static void ClearStamp(string userData)
        {
            try
            {
                var path = StampPath(userData);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// Text of the helper that waits for <paramref name="pid"/> to exit, deletes
        /// <paramref name="cacheRoot"/> and relaunches <paramref name="appExe"/>. Paths are baked in
        /// as literals (no positional args) so spaces are safe - the install dir is
        /// "…\Programs\Conditioning Control Panel". The head writes it as UTF-8 without BOM.
        /// </summary>
        public static string BuildRepairHelperScript(string cacheRoot, string appExe, string logPath, int pid)
        {
            var lines = new[]
            {
                "@echo off",
                "setlocal enableextensions",
                $"set \"LOG={logPath}\"",
                $"set \"APPEXE={appExe}\"",
                $"set \"CACHE={cacheRoot}\"",
                $"echo [native-repair] start pid={pid} > \"%LOG%\"",
                // Wait for us to exit so the loaded image sections are released; without this
                // the rd below silently leaves the mapped DLLs behind. Capped at ~1min so a
                // recycled PID cannot hang the helper forever.
                "set /a tries=0",
                ":waitloop",
                $"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \"{pid}\" >nul",
                "if errorlevel 1 goto gone",
                "set /a tries+=1",
                "if %tries% GEQ 30 (",
                "  echo [native-repair] wait timed out, proceeding anyway >> \"%LOG%\"",
                "  goto gone",
                ")",
                "ping 127.0.0.1 -n 2 >nul",
                "goto waitloop",
                ":gone",
                "echo [native-repair] app exited (tries=%tries%), clearing cache >> \"%LOG%\"",
                "rd /s /q \"%CACHE%\" 2>nul",
                "if exist \"%CACHE%\" (",
                "  echo [native-repair] WARNING cache survived the delete >> \"%LOG%\"",
                ") else (",
                "  echo [native-repair] cache cleared >> \"%LOG%\"",
                ")",
                "echo [native-repair] relaunching app >> \"%LOG%\"",
                "start \"\" \"%APPEXE%\"",
                "echo [native-repair] done >> \"%LOG%\"",
                "endlocal",
            };

            return string.Join("\r\n", lines) + "\r\n";
        }
    }
}
