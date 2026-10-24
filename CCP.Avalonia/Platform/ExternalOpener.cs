using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Services;
using Serilog;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

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

        // WPF BrowserLauncher: keep the Windows launch strategies behind this head's OS seam.
        internal static Func<string, string, bool> BrowserFallback = (file, args) =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(file, args)
                    { UseShellExecute = false, CreateNoWindow = true });
                return true;
            }
            catch { return false; }
        };

        private static MessageDialog? _browserPrompt;
        internal static void CloseBrowserPrompt() => _browserPrompt?.Close();

        internal static Func<Window, string, string, Task> BrowserPrompt = async (owner, url, purpose) =>
        {
            try { if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(url); } catch { }
            var dialog = new MessageDialog(Loc.Get("title_open_link_in_browser"),
                Loc.GetF("msg_browser_no_default_for", purpose) + Loc.GetF("msg_browser_link_copied", url), showCancel: false);
            _browserPrompt = dialog;
            try { await dialog.ShowDialogSafe(owner); }
            finally { if (ReferenceEquals(_browserPrompt, dialog)) _browserPrompt = null; }
        };

        internal static async Task<bool> OpenBrowserOrPromptAsync(Window owner, string url, string purpose)
        {
            if (!Allowed(url)) return false;
            if (await OpenAsync(owner, url)) return true;
            if (OperatingSystem.IsWindows() &&
                (BrowserFallback("explorer.exe", url) ||
                 BrowserFallback("cmd.exe", $"/c start \"\" \"{url}\"") ||
                 BrowserFallback("rundll32.exe", $"url.dll,FileProtocolHandler {url}"))) return true;
            await BrowserPrompt(owner, url, purpose);
            return false;
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
