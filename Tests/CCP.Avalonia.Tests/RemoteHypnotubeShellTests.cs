using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell half of WPF play_hypnotube (RemoteControlService.cs:1413 -> MainWindow.Browser.cs:3188):
/// the controller's page opens over the remote overlay, stop_video and a panic put the blindfold back, a
/// screen-owning host, no engine and offline refuse (restack b225, #4403).</summary>
public sealed class RemoteHypnotubeShellTests
{
    private sealed class LogSink : Serilog.Core.ILogEventSink
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<Serilog.Events.LogEvent> Events = new();
        public void Emit(Serilog.Events.LogEvent e) => Events.Enqueue(e);
    }

    private static string? Play(string url) => RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url });

    [Fact]
    public Task Controller_video_lifts_the_blindfold_and_stop_panic_offline_and_busy_screens_hold_it() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var offline = s.OfflineMode;
        var (oldPlay, oldStop) = (RemoteCommands.PlayHypnotube, RemoteCommands.StopBrowserVideo);
        const string video = "https://hypnotube.com/video/example-123.html";
        MainShellWindow? shell = null;
        Func<string, string?>? hookAfterClose = null;
        try
        {
            s.OfflineMode = false;
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(RemoteCommands.PlayHypnotube);
            var settings = shell.Named<SettingsTabView>("SettingsTab")!;
            var browser = settings.BrowserContainer;
            var web = settings.FindControl<WebHost>("BrowserWebHost")!;
            var overlay = shell.Named<Border>("RemoteControlOverlay")!;
            // The controller is here: the overlay is up and the browser sits under it (WPF :904).
            overlay.IsVisible = true;
            browser.IsVisible = false;

            // No web engine (headless): refused, never an "ok" for a video that cannot play; the blindfold stays.
            Assert.False(web.HasEngine);
            Assert.Equal(Loc.Get("remote_browser_unavailable"), Play(video));
            Assert.False(browser.IsVisible);
            Assert.Equal(0, web.NavigationRequests);

            // A page engine from here on.
            var scripts = new List<string>();
            web.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
            web.NavigationInvoker = _ => true;

            Assert.Null(Play(video));
            Assert.True(browser.IsVisible);
            Assert.False(shell.BrowserCovered);   // the overlay stops covering while the controller's video plays
            Assert.Equal(new Uri(video), web.Source);

            // stop_video: back to the site home and under the overlay again (WPF :1410, :995).
            Assert.Null(RemoteCommands.Execute("stop_video", null));
            Assert.False(browser.IsVisible);
            Assert.True(shell.BrowserCovered);
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);
            Assert.Contains(WebHostTakeover.StopScript, scripts);

            // A second stop with no controller video leaves the page the user browses alone.
            web.Navigate(new Uri(video));
            Assert.Null(RemoteCommands.Execute("stop_video", null));
            Assert.Equal(new Uri(video), web.Source);

            // The local panic ends the controller's video too (WPF MainWindow.xaml.cs:2066 PanicEndBrowserMedia).
            Assert.Null(Play(video));
            Assert.True(browser.IsVisible);
            PanicSurfaces.All.Single(x => x.Id == "browser-media").Stop(shell);
            Assert.False(browser.IsVisible);
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);

            // Graded Intake owns the screen: refused, the blindfold stays, the page is untouched (WPF Browser.cs:3195).
            var intake = new IntakeHostWindow();
            try
            {
                intake.Show();
                Assert.Equal(Loc.GetF("remote_browser_screen_busy", "Graded Intake"), Play(video));
                Assert.False(browser.IsVisible);
                Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);
            }
            finally { intake.Close(); }

            // Offline: the controller is told, the blindfold stays, nothing navigates.
            s.OfflineMode = true;
            var requests = web.NavigationRequests;
            Assert.Equal(Loc.Get("remote_browser_unavailable"), Play(video));
            Assert.False(browser.IsVisible);
            Assert.Equal(requests, web.NavigationRequests);
            s.OfflineMode = false;

            // Logs keep the host only: a controller's link or a signed page never reaches a bug report.
            var sink = new LogSink();
            var oldLogger = Serilog.Log.Logger;
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
            try
            {
                Assert.True(shell.NavigateToUrlInBrowser("https://hypnotube.com/video/private-title-123.html?token=private-secret", true));
                var logged = string.Join("\n", sink.Events.Select(e => e.RenderMessage() + e.Exception));
                Assert.Contains("hypnotube.com", logged);
                Assert.DoesNotContain("private-title", logged);
                Assert.DoesNotContain("private-secret", logged);
            }
            finally { Serilog.Log.Logger = oldLogger; }
        }
        finally
        {
            shell?.Close();
            hookAfterClose = RemoteCommands.PlayHypnotube;
            (RemoteCommands.PlayHypnotube, RemoteCommands.StopBrowserVideo) = (oldPlay, oldStop);
            s.OfflineMode = offline;
        }
        Assert.Null(hookAfterClose);   // the shell lets go of the hook on close
        return Task.CompletedTask;
    });
}
