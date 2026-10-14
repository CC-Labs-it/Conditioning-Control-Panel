using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Opens Windows Explorer on a file or folder.
    ///
    /// <para>Every "Reveal in Explorer" / "Open folder" button in the app used to hand-roll
    /// <c>Process.Start("explorer.exe", $"/select,\"{path}\"")</c>, and on a mismatch explorer
    /// silently falls back to opening a default folder instead of reporting an error - so the
    /// button looked like it did nothing useful (ccp-bugs #998, media on D: while the app runs
    /// from C:).</para>
    ///
    /// <para>The #998 fix handed <c>/select,&lt;path&gt;</c> to
    /// <see cref="ProcessStartInfo.ArgumentList"/> as a single token so the runtime would apply the
    /// correct Windows quoting rules. It does - and that is exactly the bug. ArgumentList wraps a
    /// token in double quotes when it contains whitespace, and it wraps the WHOLE token, so a path
    /// with spaces came out as <c>explorer.exe "/select,D:\Conditioning Control Panel\..."</c> with
    /// the switch trapped inside the quotes. Explorer reads that as one nonsense location and falls
    /// back to Documents. Paths without spaces are never quoted, which is why the button kept
    /// working for most people and on the dev's machine, and why #998 looked fixed (ccp-bugs
    /// #1108).</para>
    ///
    /// <para>So the command line is built by hand: the switch stays OUTSIDE the quotes and only the
    /// path is quoted, which is the syntax explorer actually expects. Windows filenames cannot
    /// contain a double quote, so a path carrying one is rejected rather than escaped - escaping it
    /// could only ever build a command line that points somewhere else.</para>
    /// </summary>
    public static class ExplorerLauncher
    {
        /// <summary>
        /// Opens Explorer with <paramref name="path"/> selected. If the file no longer exists,
        /// falls back to opening its containing directory. Returns true if Explorer was launched.
        /// Never throws.
        /// </summary>
        public static bool RevealInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                if (File.Exists(path))
                {
                    var arguments = BuildRevealArguments(Path.GetFullPath(path));
                    return arguments != null && Launch(arguments);
                }

                if (Directory.Exists(path))
                {
                    var arguments = BuildFolderArguments(Path.GetFullPath(path));
                    return arguments != null && Launch(arguments);
                }

                // File is gone (deleted / drive detached) - settle for its folder.
                return OpenFolder(Path.GetDirectoryName(path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: reveal failed for {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Opens <paramref name="directory"/> in Explorer. Returns true if Explorer was launched.
        /// Never throws.
        /// </summary>
        public static bool OpenFolder(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            try
            {
                if (!Directory.Exists(directory))
                {
                    Log.Debug("ExplorerLauncher: folder no longer exists: {Dir}", directory);
                    return false;
                }

                var arguments = BuildFolderArguments(Path.GetFullPath(directory));
                return arguments != null && Launch(arguments);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: open folder failed for {Dir}", directory);
                return false;
            }
        }

        /// <summary>Command line that opens Explorer on <paramref name="fullPath"/>'s folder with the file
        /// selected; null if the path cannot be expressed safely. The one copy is in CCP.Core.</summary>
        internal static string? BuildRevealArguments(string fullPath) => RevealArguments.ExplorerSelect(fullPath);

        /// <summary>Command line that opens Explorer on the folder <paramref name="fullPath"/>; null as above.</summary>
        internal static string? BuildFolderArguments(string fullPath) => RevealArguments.ExplorerFolder(fullPath);

        private static bool Launch(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    // Pre-built command line, so Arguments rather than ArgumentList - see the class
                    // remarks. UseShellExecute = false keeps the string we built verbatim.
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: explorer.exe failed to start for {Args}", arguments);
                return false;
            }
        }
    }
}
