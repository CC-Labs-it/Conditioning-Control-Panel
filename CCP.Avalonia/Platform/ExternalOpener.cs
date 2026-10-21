using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The only way this head opens anything outside the app: a web link, a file or a folder. Every launch
    /// asks <see cref="SandboxNet.Allows"/> first, so a CCP_USERDATA_DIR sandbox (tests, kc, render-all)
    /// never opens a real site; a refused launch is logged. <c>SandboxNetTests.EveryLaunchGoesThroughExternalOpener</c>
    /// fails when a new Process.Start(UseShellExecute) / Launcher / NavigateUri call bypasses it.
    /// </summary>
    internal static class ExternalOpener
    {
        /// <summary>Test seam for the shell half (Process.Start with UseShellExecute: xdg-open on Linux).</summary>
        internal static Func<string, bool> Shell = target =>
        {
            using var p = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        };

        /// <summary>Outside a sandbox everything non-empty goes to the shell, as before. Inside one: local paths, and
        /// URIs by <see cref="SandboxNet.Allows"/>; never a UNC path or file://host (that is a network share).</summary>
        internal static bool Allowed(string? target) => Allowed(target, SandboxNet.Active);

        internal static bool Allowed(string? target, bool sandboxed)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (!sandboxed) return true;
            var unc = target.StartsWith(@"\\", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal);
            if (!unc && Path.IsPathRooted(target) && !target.Contains("://")) return true;
            if (!unc && Uri.TryCreate(target, UriKind.Absolute, out var uri) && SandboxNet.Allows(uri, sandboxed)) return true;
            // Host only: a signed URL's query string must not reach the log.
            Log.Warning("Sandbox: refused to open {Host}", Uri.TryCreate(target, UriKind.Absolute, out var u) ? u.Host : "(not a URL)");
            return false;
        }

        /// <summary>Shell-open <paramref name="target"/> (WPF Process.Start with UseShellExecute). False when refused or failed.</summary>
        public static bool Open(string? target)
        {
            if (!Allowed(target)) return false;
            try { return Shell(target!); }
            catch (Exception ex) { Log.Warning("Could not open {Target}: {Error}", Path.IsPathRooted(target!) ? target : "link", ex.Message); return false; }
        }

        /// <summary>Test seam for the select-the-file half of <see cref="Reveal"/>: gets a full path to an existing
        /// file, true when the file manager took it.</summary>
        internal static Func<string, bool> SelectInFileManager = SelectNative;

        /// <summary>
        /// WPF ExplorerLauncher.RevealInExplorer: the file manager with <paramref name="path"/> selected; a folder
        /// opens as itself; a file that is gone (deleted, drive detached) settles for its folder (#998). A desktop
        /// that cannot select (no FileManager1 service) also gets the folder. False when nothing opened. Never throws.
        /// </summary>
        public static bool Reveal(string? path)
        {
            if (!Allowed(path)) return false;
            try
            {
                if (File.Exists(path))
                {
                    var full = Path.GetFullPath(path!);
                    return SelectInFileManager(full) || Open(Path.GetDirectoryName(full));
                }
                if (Directory.Exists(path)) return Open(Path.GetFullPath(path!));
                var dir = Path.GetDirectoryName(path);
                return Directory.Exists(dir) && Open(dir);
            }
            catch (Exception ex) { Log.Warning("[Reveal] failed: {Error}", ex.Message); return false; }
        }

        /// <summary>Explorer <c>/select</c> on Windows (compiled here, not run on Linux CI), Finder <c>open -R</c> on
        /// macOS, the freedesktop FileManager1.ShowItems call elsewhere. The argument strings live in
        /// <see cref="ConditioningControlPanel.Helpers.RevealArguments"/>.</summary>
        private static bool SelectNative(string full)
        {
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsWindows())
            {
                // A pre-built command line, so Arguments rather than ArgumentList (see RevealArguments).
                psi.FileName = "explorer.exe";
                psi.Arguments = ConditioningControlPanel.Helpers.RevealArguments.ExplorerSelect(full);
                if (psi.Arguments == null) return false;
                using var _ = Process.Start(psi);
                return true;
            }
            psi.FileName = OperatingSystem.IsMacOS() ? "open" : "dbus-send";
            foreach (var a in OperatingSystem.IsMacOS() ? new[] { "-R", full } : ConditioningControlPanel.Helpers.RevealArguments.FreedesktopShowItems(full))
                psi.ArgumentList.Add(a);
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            try
            {
                using var p = Process.Start(psi);
                // dbus-send --print-reply answers at once; a missing service is a non-zero exit, so the folder opens instead.
                // A file manager still starting after the wait counts as taken: falling back would open a second window.
                return p != null && (!p.WaitForExit(3000) || p.ExitCode == 0);
            }
            catch (System.ComponentModel.Win32Exception) { return false; }   // swallow: no dbus-send / open here, the folder opens instead
        }

        /// <summary>Open through the window's platform launcher (portal-aware), else the shell. False when refused or failed.</summary>
        public static async Task<bool> OpenAsync(TopLevel? top, string? target)
        {
            if (!Allowed(target)) return false;
            try
            {
                if (top?.Launcher is { } l && (Directory.Exists(target)
                        ? await l.LaunchDirectoryInfoAsync(new DirectoryInfo(target!))
                        : await l.LaunchUriAsync(Path.IsPathRooted(target!) ? new Uri(Path.GetFullPath(target!)) : new Uri(target!))))
                    return true;
            }
            catch (Exception ex) { Log.Debug("Launcher failed, trying the shell: {Error}", ex.Message); }
            return Open(target);
        }
    }
}
