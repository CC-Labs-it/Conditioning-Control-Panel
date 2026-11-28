using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Browser.cs:1089-1240: the Home browser's media messages reach the session,
/// the session holds the screen for bubble counts and Takeover, and panic ends it.</summary>
public sealed class BrowserMediaRoutingTests
{
    [Fact]
    public Task HomeMessagesDriveTheSessionAndPanicEndsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var media = BrowserMediaSession.Shared;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };

            // The reporter rides every completed navigation (WPF injects it on every document).
            host.OnNavigationCompleted(new Uri("https://127.0.0.1/"), success: true);
            Assert.Contains(MainShellWindow.BrowserMediaReporterScript, scripts);

            // User playback: the session opens and holds bubble counts and Takeover.
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"pos\":0,\"dur\":120}");
            Assert.Equal(BrowserMediaSession.MediaOwner.User, media.Owner);
            Assert.True(BubbleCountHost.Instance.OtherInteractionActive);
            Assert.True(shell.Autonomy.IsBusy());
            // A JSON-quoted carrier string is the same message.
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize("{\"type\":\"ccpMedia\",\"state\":\"stopped\",\"reason\":\"ended\"}"));
            Assert.False(media.HoldsScreen);
            Assert.False(BubbleCountHost.Instance.OtherInteractionActive);

            // Takeover messages: fsExit hands the video to the user, videoEnded ends it.
            media.ReplaceSession(BrowserMediaSession.MediaOwner.Remote, takeover: true);
            host.OnWebMessage("{\"type\":\"videoDuration\",\"seconds\":30}");
            Assert.True(media.IsTakeover);
            host.OnWebMessage("{\"type\":\"fsExit\"}");
            Assert.False(media.IsTakeover);
            Assert.Equal(BrowserMediaSession.MediaOwner.User, media.Owner);
            host.OnWebMessage("{\"type\":\"videoEnded\"}");
            Assert.False(media.HoldsScreen);

            // Panic ends an open session and its heartbeat tick within the surface call.
            host.OnWebMessage("{\"type\":\"videoStarted\"}");
            Assert.True(media.TickArmed);
            PanicSurfaces.All.Single(x => x.Id == "browser-media").Stop(shell);
            Assert.False(media.HoldsScreen);
            Assert.False(media.TickArmed);

            // Without a shell the surface still ends the session.
            media.OnMediaPlaying(0);
            PanicSurfaces.All.Single(x => x.Id == "browser-media").Stop(null);
            Assert.False(media.HoldsScreen);
        }
        finally
        {
            media.OnMediaStopped("test");
            shell.Close();
        }
        return Task.CompletedTask;
    });
}
