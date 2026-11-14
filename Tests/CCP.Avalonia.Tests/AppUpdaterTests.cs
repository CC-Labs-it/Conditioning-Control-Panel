using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The updater on Linux: sandbox stays offline, WPF's pending outcome is consumed, the pill
/// names the fake newer version and clicking it opens the releases page without downloading.</summary>
public sealed class AppUpdaterTests
{
    private sealed class FakeFeed : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            const string json = """
                {"tag_name":"v99.1.0","body":"notes","assets":[{"name":"ConditioningControlPanel-99.1.0-Setup.exe","size":1234,
                 "browser_download_url":"http://127.0.0.1:9/dl/ConditioningControlPanel-99.1.0-Setup.exe"}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/api", true)]
    [InlineData("http://[::1]:9/api", true)]
    [InlineData("http://localhost:9/api", true)]
    [InlineData("http://loopback:9/", false)]
    [InlineData("http://u:p@127.0.0.1/", false)]
    [InlineData("ftp://127.0.0.1/", false)]
    [InlineData("https://api.github.com/x", false)]
    public void OnlyLiteralLoopbackOverridesAreHonoured(string url, bool honoured)
    {
        // One Core rule for CCP_UPDATE_API_URL and CCP_CONTENT_BASE_URL.
        Assert.Equal(honoured, LoopbackUrl.IsHonoured(url, out _));
        try
        {
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", url);
            Assert.Equal(honoured, AppUpdater.ApiBase() != null);   // sandboxed: refused = no network
        }
        finally { Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null); }
    }

    [Fact]
    public async Task LinuxPillShowsVersionOpensPageAndNeverDownloads()
    {
        // Windows CI is not an Inno install (no unins000/registry key), so CheckAsync stays quiet there.
        if (OperatingSystem.IsWindows()) Assert.Skip("Linux notify path; the Windows install path needs an Inno install.");
        var feed = new FakeFeed();
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldOpen = AppUpdater.OpenUrl;
        var oldAccepted = CoreSettings.Current.HasAcceptedAgeVerification;
        string? opened = null;
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                // A sandbox with no loopback feed never reaches GitHub.
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
                Assert.Null(AppUpdater.ApiBase());
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "https://api.github.com/x");
                Assert.Null(AppUpdater.ApiBase());
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
                Assert.Equal("http://127.0.0.1:9/api", AppUpdater.ApiBase());

                AppUpdater.Handler = feed;
                AppUpdater.OpenUrl = (_, url) => { opened = url; return Task.CompletedTask; };
                CoreReleaseContent.AppVersionProvider = () => "6.11.3";

                // WPF's marker from a run that installed fine: consumed silently on first run.
                File.WriteAllText(ReleaseFeed.AttemptFilePath(CorePaths.UserData), "6.11.3");
                File.WriteAllText(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData), "0");

                CoreSettings.Current.HasAcceptedAgeVerification = false;
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                var shell = new MainShellWindow();
                StartupLadder.ModalWindowReady = () => shell.IsVisible;
                shell.Show();
                var prior = new TaskCompletionSource();
                StartupLadder.EnqueueModal("prior", 20, () => prior.Task);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
                for (var i = 0; i < 5; i++) await Task.Yield();
                var startup = AppUpdater.StartupAsync(shell);
                await Task.Delay(1500);
                Assert.Null(AppUpdater.OpenDialog);   // waits behind the age gate
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                await startup;
                Assert.True(MainShellWindow.IsStartupDialogShowing);
                Assert.Null(AppUpdater.OpenDialog);
                prior.SetResult();
                for (var i = 0; i < 5; i++) await Task.Yield();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
                for (var i = 0; i < 100 && AppUpdater.OpenDialog is null; i++) await Task.Delay(50);
                var dialog = AppUpdater.OpenDialog;
                Assert.NotNull(dialog);   // offered once at startup, as WPF App.xaml.cs:4903
                Assert.Equal(Loc.Get("btn_download_installer_manually"),
                    ((TextBlock)dialog!.FindControl<Button>("BtnInstall")!.Content!).Text);   // Linux: notify variant

                Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(CorePaths.UserData)));
                Assert.False(File.Exists(ReleaseFeed.AttemptResultFilePath(CorePaths.UserData)));
                var pill = shell.Named<Button>("BtnUpdateAvailable")!;
                Assert.Equal(Loc.GetF("btn_update_to_version", "99.1.0"), pill.Content);
                Assert.Equal(Loc.GetF("tooltip_update_to_version_download", "99.1.0"), ToolTip.GetTip(pill));

                await AppUpdater.PillClickedAsync(shell);
                Assert.Equal(ReleaseLinks.ReleasesPageUrl, opened);
                // Only the feed was read: no releases/tags lookup, no asset GET.
                Assert.Equal(new[] { "/api/releases/latest" }, feed.Requests);

                // "Later": the version is skipped for 24h, so the next startup neither lights nor asks.
                dialog.FindControl<Button>("BtnLater")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData) != "99.1.0"; i++) await Task.Delay(50);
                Assert.Equal("99.1.0", ReleaseFeed.GetSkippedUpdateVersion(CorePaths.UserData));
                var shell2 = new MainShellWindow();
                shell2.Show();
                await AppUpdater.StartupAsync(shell2);
                Assert.Null(AppUpdater.OpenDialog);
                Assert.NotEqual(Loc.GetF("btn_update_to_version", "99.1.0"), shell2.Named<Button>("BtnUpdateAvailable")!.Content);
                shell2.Close();
                shell.Close();
            }
            finally
            {
                AppUpdater.Handler = null;
                AppUpdater.OpenUrl = oldOpen;
                CoreReleaseContent.AppVersionProvider = oldVersion;
                CoreSettings.Current.HasAcceptedAgeVerification = oldAccepted;
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
            }
        });
    }

    [Theory, InlineData(false), InlineData(true)]
    public Task FailedUpdateReportWaitsForTheEarlierModal(bool tray) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var version = CoreReleaseContent.AppVersionProvider;
        var accepted = CoreSettings.Current.HasAcceptedAgeVerification;
        MainShellWindow? shell = null;
        try
        {
            CoreReleaseContent.AppVersionProvider = () => "99.1.0";
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
            AppUpdater.Handler = new FakeFeed();
            ReleaseFeed.SetPendingUpdateAttempt(CorePaths.UserData, "99.2.0");
            shell = new MainShellWindow();
            var ready = true;   // tray: the ladder never sees a ready panel and abandons the report
            StartupLadder.ModalWindowReady = () => ready && shell.IsVisible;
            shell.Show();
            var prior = new TaskCompletionSource();
            StartupLadder.EnqueueModal("prior", 5, () => prior.Task);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
            for (var i = 0; i < 5; i++) await Task.Yield();
            await AppUpdater.StartupAsync(shell);
            Assert.Empty(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.UpdateFailedDialog>());
            ready = !tray;
            prior.SetResult();
            for (var i = 0; i < 5; i++) await Task.Yield();
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
            for (var i = 0; tray && i <= (int)(StartupLadder.ModalMaxWait / StartupLadder.ModalPoll); i++)
            { ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalPoll); for (var j = 0; j < 5; j++) await Task.Yield(); }
            for (var i = 0; i < 100 && !shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.UpdateFailedDialog>().Any(); i++) await Task.Delay(20);
            Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.UpdateFailedDialog>()).Close();
        }
        finally
        {
            shell?.Close();
            AppUpdater.Handler = null;
            CoreReleaseContent.AppVersionProvider = version;
            CoreSettings.Current.HasAcceptedAgeVerification = accepted;
            ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
        }
    });

    [Fact]
    public async Task LauncherChipOffersOnceBeforeStartupAndFollowsLockdown()
    {
        var (installed, offered, latest) = (AppUpdater.Installed, AppUpdater._offered, AppUpdater.Latest);
        var (offline, motion, accepted, version, lockdown) = (CoreSettings.Current.OfflineMode, CoreSettings.Current.MotionLevel,
            CoreSettings.Current.HasAcceptedAgeVerification, CoreReleaseContent.AppVersionProvider, LockdownService.Current);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            AppUpdater.Installed = () => true;
            AppUpdater._offered = false;
            CoreSettings.Current.OfflineMode = false;
            AppUpdater.Latest = null;
            AppUpdater.Handler = new FakeFeed();
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
            CoreReleaseContent.AppVersionProvider = () => "6.11.3";
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            var launcher = new LauncherWindow();
            try
            {
                shell.Show();
                launcher.Show();
                var chip = launcher.FindControl<Button>("UpdateChip")!;
                Assert.False(chip.IsVisible);
                var startup = AppUpdater.StartupAsync(shell);
                for (var i = 0; i < 100 && !chip.IsVisible; i++) await Task.Delay(50);
                Assert.True(chip.IsVisible);
                Assert.Equal(Loc.GetF("launcher_update_tooltip", "99.1.0"), ToolTip.GetTip(chip));
                ld.Activate(TimeSpan.FromMinutes(30));
                Assert.False(chip.IsVisible);
                ld.Deactivate();
                Assert.True(chip.IsVisible);
                chip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && AppUpdater.OpenDialog is null; i++) await Task.Delay(50);
                Assert.NotNull(AppUpdater.OpenDialog);
                AppUpdater.OpenDialog!.FindControl<Button>("BtnLater")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                Assert.Same(startup, await Task.WhenAny(startup, Task.Delay(3000)));
                Assert.Null(AppUpdater.OpenDialog);
            }
            finally
            {
                if (ld.IsActive) ld.Deactivate();
                launcher.Close(); shell.Close();
                AppUpdater.Handler = null;
                ReleaseFeed.ClearSkippedUpdateVersion(CorePaths.UserData);
                Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", null);
                (AppUpdater.Installed, AppUpdater._offered, AppUpdater.Latest) = (installed, offered, latest);
                (CoreSettings.Current.OfflineMode, CoreSettings.Current.MotionLevel, CoreSettings.Current.HasAcceptedAgeVerification) = (offline, motion, accepted);
                CoreReleaseContent.AppVersionProvider = version;
                LockdownService.Current = lockdown;
            }
        });
    }

    [Fact]
    public async Task UninstalledWindowsCheckStaysOffline()
    {
        var feed = new FakeFeed();
        var oldApi = Environment.GetEnvironmentVariable("CCP_UPDATE_API_URL");
        var (installed, offline) = (AppUpdater.Installed, CoreSettings.Current.OfflineMode);
        try
        {
            AppUpdater.Installed = () => false;
            AppUpdater.Handler = feed;
            CoreSettings.Current.OfflineMode = false;
            Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", "http://127.0.0.1:9/api");
            Assert.Null(await AppUpdater.CheckAsync(force: true));
            Assert.Empty(feed.Requests);
        }
        finally
        {
            AppUpdater.Handler = null; Environment.SetEnvironmentVariable("CCP_UPDATE_API_URL", oldApi);
            (AppUpdater.Installed, CoreSettings.Current.OfflineMode) = (installed, offline);
        }
    }

    [Fact]
    public void LauncherChipMotionUsesMatchingWpfCurves() => AvaloniaTestDispatcher.Run(() =>
    {
        var (motion, gentle, probe, stamp, lockdown, latest) = (CoreSettings.Current.MotionLevel, CoreSettings.Current.GentleVisuals,
            UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp, LockdownService.Current, AppUpdater.Latest);
        CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
        CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        LockdownService.Current = null;
        AppUpdater.Latest = null;
        var launcher = new LauncherWindow();
        try
        {
            launcher.Show();
            var chip = launcher.FindControl<Button>("UpdateChip")!;
            var scale = Assert.IsType<ScaleTransform>(chip.RenderTransform);
            AppUpdater.Latest = new ConditioningControlPanel.Models.UpdateInfo { Version = "99.1.0", IsNewer = true };
            var x = scale.Transitions!.OfType<DoubleTransition>().Single(t => t.Property == ScaleTransform.ScaleXProperty);
            var y = scale.Transitions!.OfType<DoubleTransition>().Single(t => t.Property == ScaleTransform.ScaleYProperty);
            Assert.Equal(TimeSpan.FromMilliseconds(240), x.Duration);
            Assert.Equal(x.Duration, y.Duration);
            var pop = Assert.IsType<ConditioningControlPanel.Avalonia.Theme.BackEaseOutAmplitude>(x.Easing);
            Assert.Equal(0.4, pop.Amplitude);
            Assert.Same(pop, y.Easing);
            Assert.Equal(1 - (0.5 * 0.5 * 0.5 - 0.4 * 0.5 * Math.Sin(Math.PI * 0.5)), pop.Ease(0.5), 10);

            AppUpdater.Latest = null;
            x = scale.Transitions!.OfType<DoubleTransition>().Single(t => t.Property == ScaleTransform.ScaleXProperty);
            y = scale.Transitions!.OfType<DoubleTransition>().Single(t => t.Property == ScaleTransform.ScaleYProperty);
            Assert.Equal(TimeSpan.FromMilliseconds(160), x.Duration);
            Assert.Equal(x.Duration, y.Duration);
            Assert.IsType<QuadraticEaseIn>(x.Easing);
            Assert.Same(x.Easing, y.Easing);
            Assert.Equal(0.25, x.Easing.Ease(0.5), 10);
        }
        finally
        {
            launcher.Close();
            (CoreSettings.Current.MotionLevel, CoreSettings.Current.GentleVisuals) = (motion, gentle);
            (UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp) = (probe, stamp);
            (LockdownService.Current, AppUpdater.Latest) = (lockdown, latest);
        }
    });

    /// <summary>
    /// WPF ddc9dd047 on this head: a loopback server sends the installer's headers and 1000 bytes, then
    /// goes quiet. Each attempt fails on the stall window, is retried as transient, and the third
    /// failure surfaces - bounded, instead of hanging forever. A moving download still completes.
    /// </summary>
    [Fact(Timeout = 60_000)]   // a regression hangs forever; make it a failure instead
    public async Task AStalledInstallerDownloadRetriesThenFailsInBoundedTime()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var downloads = 0;
        using var stop = new CancellationTokenSource();
        var ok = new byte[200_000];
        new Random(5).NextBytes(ok);
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                _ = Task.Run(async () =>
                {
                    using var c = client;
                    var s = c.GetStream();
                    var head = new System.Text.StringBuilder();
                    var one = new byte[1];
                    while (!head.ToString().EndsWith("\r\n\r\n") && await s.ReadAsync(one) > 0) head.Append((char)one[0]);
                    var path = head.ToString().Split(' ')[1];
                    async Task Send(string type, byte[] body, long length = -1)
                    {
                        if (length < 0) length = body.Length;
                        await s.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n"));
                        await s.WriteAsync(body);
                        await s.FlushAsync();
                    }
                    if (path.StartsWith("/api/releases/tags/v"))
                    {
                        var v = path["/api/releases/tags/v".Length..];
                        await Send("application/json", System.Text.Encoding.UTF8.GetBytes(
                            $"{{\"assets\":[{{\"browser_download_url\":\"http://127.0.0.1:{port}/dl/ConditioningControlPanel-{v}-Setup.exe\"}}]}}"));
                    }
                    else if (path.Contains("-99.1.0-"))
                    {
                        Interlocked.Increment(ref downloads);
                        await Send("application/octet-stream", new byte[1000], 1_000_000);   // promises 1 MB, sends 1000 bytes
                        try { await s.ReadAsync(one, stop.Token); } catch { }   // stay silent until the client hangs up
                    }
                    else await Send("application/octet-stream", ok);
                });
            }
        });

        var oldStall = AppUpdater.StallTimeout;
        var oldDelay = AppUpdater.RetryDelay;
        var oldDir = AppUpdater.DownloadDir;
        var tmp = Directory.CreateTempSubdirectory("ccp-stall-").FullName;
        try
        {
            AppUpdater.DownloadDir = Path.Combine(tmp, "update");   // never the real %TEMP% updater folder
            AppUpdater.StallTimeout = TimeSpan.FromMilliseconds(300);
            AppUpdater.RetryDelay = _ => TimeSpan.FromMilliseconds(50);
            var api = $"http://127.0.0.1:{port}/api";

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AppUpdater.DownloadInstallerAsync(api, "99.1.0", _ => { }));
            clock.Stop();
            Assert.IsType<TimeoutException>(ex.InnerException);
            Assert.Contains("after 3 attempts", ex.Message);
            Assert.Equal(3, Volatile.Read(ref downloads));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"took {clock.Elapsed}");

            var path = await AppUpdater.DownloadInstallerAsync(api, "99.2.0", _ => { });
            Assert.Equal(ok, File.ReadAllBytes(path));
        }
        finally
        {
            AppUpdater.StallTimeout = oldStall;
            AppUpdater.RetryDelay = oldDelay;
            AppUpdater.DownloadDir = oldDir;
            try { Directory.Delete(tmp, true); } catch { }
            stop.Cancel();
            listener.Stop();
        }
    }
}
