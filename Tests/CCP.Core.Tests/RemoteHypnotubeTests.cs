using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]   // trigger_panic and StopEffects touch the statics RemoteRelayTests uses
public class RemoteHypnotubeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://hypnotube.com/")]
    [InlineData("https://hypnotube.com.evil.test/video/123")]
    [InlineData("file://hypnotube.com/video/123")]
    public void Rejects_ineligible_controller_links(string? url)
    {
        var (oldPlay, played) = (RemoteCommands.PlayHypnotube, new List<string>());
        RemoteCommands.PlayHypnotube = u => { played.Add(u); return null; };
        try
        {
            Assert.Equal("not an eligible HypnoTube URL", RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));
            Assert.Empty(played);   // a bad link never reaches the browser
        }
        finally { RemoteCommands.PlayHypnotube = oldPlay; }
    }

    [Theory]
    [InlineData("https://hypnotube.com/video/123")]
    [InlineData("https://www.hypnotube.com/video/example-123.html")]
    public void Eligible_links_go_to_the_head_browser_and_its_refusal_comes_back(string url)
    {
        var oldPlay = RemoteCommands.PlayHypnotube;
        try
        {
            RemoteCommands.PlayHypnotube = null;   // a head with no browser says so
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));

            string? got = null;
            RemoteCommands.PlayHypnotube = u => { got = u; return null; };
            Assert.Null(RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));
            Assert.Equal(url, got);

            RemoteCommands.PlayHypnotube = _ => "the browser could not open it";
            Assert.Equal("the browser could not open it", RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));
        }
        finally { RemoteCommands.PlayHypnotube = oldPlay; }
    }

    [Fact]
    public void Start_flash_leaves_the_controller_browser_video_open()
    {
        var oldStop = RemoteCommands.StopBrowserVideo;
        var enabled = CoreSettings.Current.FlashEnabled;
        var wasRunning = CoreFlash.IsRunning;
        var stops = 0;
        RemoteCommands.StopBrowserVideo = () => stops++;
        try
        {
            Assert.Null(RemoteCommands.Execute("start_flash", null));
            Assert.Equal(0, stops);
            Assert.True(CoreFlash.IsRunning);
            Assert.True(CoreSettings.Current.FlashEnabled);
        }
        finally
        {
            CoreFlash.Stop();
            RemoteCommands.StopBrowserVideo = oldStop;
            CoreSettings.Current.FlashEnabled = enabled;
            if (wasRunning) CoreFlash.Start();
        }
    }

    // WPF RemoteControlService.cs:1410 (stop_video), :1001 (panic, session end) and :1164 (controller left).
    [Fact]
    public void Stop_video_panic_and_leave_stop_the_browser_video()
    {
        var s = CoreSettings.Current;
        var (strict, panic) = (s.StrictLockEnabled, s.PanicKeyEnabled);
        var (oldStop, stops) = (RemoteCommands.StopBrowserVideo, 0);
        RemoteCommands.StopBrowserVideo = () => stops++;
        try
        {
            Assert.Null(RemoteCommands.Execute("stop_video", null));
            Assert.Equal(1, stops);
            RemoteCommands.StopEffects(force: false);
            Assert.Equal(2, stops);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.Equal(3, stops);
        }
        finally
        {
            RemoteCommands.StopBrowserVideo = oldStop;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (strict, panic);
        }
    }
}
