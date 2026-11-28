using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--webview-check [--expect-blocked]`: the real WebView2 path, which `--smoke` never reaches (headless, so
    /// WebHost.IsAvailable is false). Opens a desktop window with a WebHost set up like the Home card
    /// (SettingsTabView.axaml BrowserWebHost: no Scrolller gate, offline block, construction retry, ad block),
    /// loads one page of this app's loopback server and one https page, and passes only if each navigation
    /// completed successfully, document.title is non-empty and the WebView2 controller reports IsVisible.
    /// --expect-blocked refuses the https page through AllowNavigation and must fail: proof the check can fail.
    /// Linux prints "skipped: windows only" and exits 0. Run without CCP_USERDATA_DIR: a sandbox refuses https.
    /// </summary>
    internal static class WebViewCheck
    {
        internal const string HttpsPage = "https://example.com/";
        internal static readonly TimeSpan LegTimeout = TimeSpan.FromSeconds(30);

        public static int Run(string[] args)
        {
            var expectBlocked = ExpectBlocked(args);
            if (!OperatingSystem.IsWindows()) { Console.WriteLine("skipped: windows only"); return 0; }

            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
            var fails = -1;
            Dispatcher.UIThread.Post(async () =>
            {
                Window? window = null;
                try
                {
                    var host = new WebHost { GateScrolller = false, BlockWhenOffline = true, RetryFailedConstruction = true, BlockAds = true };
                    if (expectBlocked) host.AllowNavigation = u => u.Scheme != Uri.UriSchemeHttps;
                    window = new Window { Title = "CCP webview check", Width = 1000, Height = 700, Content = host };
                    window.Show();
                    var pages = new[] { new Uri(WebAssetServer.Shared.Url("chaster/trailers.html")), new Uri(HttpsPage) };
                    fails = await Check(host, pages, ControllerVisible, Console.WriteLine, LegTimeout);
                }
                catch (Exception e) { Console.WriteLine("FAIL: webview-check threw: " + e); }
                finally { window?.Close(); lifetime.Shutdown(); }
            });
            lifetime.Start(Array.Empty<string>());
            return Exit(fails, expectBlocked, Console.WriteLine);
        }

        internal static bool ExpectBlocked(string[] args) => Array.IndexOf(args, "--expect-blocked") >= 0;

        /// <summary>fails &lt; 0 means the check itself threw, which proves nothing either way.</summary>
        internal static int Exit(int fails, bool expectBlocked, Action<string> log)
        {
            if (expectBlocked)
            {
                log(fails > 0 ? "PASS: the blocked page failed the check" : "FAIL: --expect-blocked did not fail, the check cannot fail");
                return fails > 0 ? 0 : 1;
            }
            log(fails == 0 ? "PASS" : $"FAIL ({(fails < 0 ? "threw" : fails + " mismatch(es)")})");
            return fails == 0 ? 0 : 1;
        }

        /// <summary>Loads each page in turn; one FAIL line with the reason per unmet condition.</summary>
        internal static async Task<int> Check(WebHost host, IReadOnlyList<Uri> pages, Func<WebHost, bool?> controllerVisible,
            Action<string> log, TimeSpan timeout)
        {
            var fails = 0;
            void Expect(bool ok, string what) { log($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
            foreach (var page in pages)
            {
                var name = page.Scheme + "://" + page.Authority;   // never the loopback token
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Finished(Uri url, bool success) { if (url.Authority == page.Authority) done.TrySetResult(success); }
                host.NavigationFinished += Finished;
                try
                {
                    if (!host.Navigate(page))
                    {
                        Expect(false, $"{name}: Navigate refused (engine {host.HasEngine}, gate {host.ScrolllerGate?.ToString() ?? "unreported"}, {host.FailureStatusKey})");
                        continue;
                    }
                    var completed = await Task.WhenAny(done.Task, Task.Delay(timeout)) == done.Task;
                    Expect(completed, $"{name}: navigation completed within {timeout.TotalSeconds:0} s (gate {host.ScrolllerGate?.ToString() ?? "unreported"})");
                    if (!completed) continue;
                    Expect(done.Task.Result, $"{name}: navigation succeeded");
                    var title = Title(await host.InvokeScriptAsync("document.title"));
                    Expect(!string.IsNullOrWhiteSpace(title), $"{name}: document.title '{title}'");
                    var visible = controllerVisible(host);
                    Expect(visible == true, $"{name}: WebView2 controller IsVisible = {visible?.ToString() ?? "unreadable"}");
                }
                finally { host.NavigationFinished -= Finished; }
            }
            return fails;
        }

        // WebView2 returns script results as JSON: "\"Example Domain\"".
        internal static string? Title(string? json)
        {
            if (json is null || !json.StartsWith('"')) return json;
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(json); }
            catch (System.Text.Json.JsonException) { return json; }
        }

        // ICoreWebView2Controller vtable: IUnknown (0-2), get_IsVisible (3), put_IsVisible (4).
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int VisibleGetter(IntPtr controller, out int visible);

        private static bool? ControllerVisible(WebHost host)
        {
            if (!OperatingSystem.IsWindows() || host.NativeView?.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle win
                || win.CoreWebView2Controller == IntPtr.Zero) return null;
            var controller = win.CoreWebView2Controller;
            var getter = Marshal.ReadIntPtr(Marshal.ReadIntPtr(controller), 3 * IntPtr.Size);
            var hr = Marshal.GetDelegateForFunctionPointer<VisibleGetter>(getter)(controller, out var visible);
            return hr < 0 ? null : visible != 0;
        }
    }
}
