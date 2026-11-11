using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebHostVideoClockTests
{
    [Fact]
    public void PollTracksVideoAndDetaches() => AvaloniaTestDispatcher.Run(() =>
    {
        var web = new WebHost();
        var scripts = new List<string>();
        web.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
        using var clock = new WebHostVideoClock(web);
        var ticks = new List<double>();
        clock.PlaybackTimeChanged += ticks.Add;
        clock.Attach();
        var id = Regex.Match(scripts[0], "ccpVideoClock:'([0-9a-f]+)'").Groups[1].Value;
        Assert.NotEmpty(id);
        void Send(string body) => web.OnWebMessage(body.Replace("ID", id));
        Send("{\"ccpVideoClock\":\"other\",\"hasVideo\":true,\"t\":99}");
        Assert.Equal(0, clock.GetCurrentTimeSeconds());
        Send("{\"ccpVideoClock\":\"ID\",\"hasVideo\":true,\"t\":12,\"d\":40,\"playing\":true}");
        Assert.True(clock.IsPlaying);
        Assert.Equal(40, clock.GetDurationSeconds());
        Assert.Contains(12, ticks);
        Thread.Sleep(40);
        Assert.True(clock.GetCurrentTimeSeconds() > 12.01);
        Send("{\"ccpVideoClock\":\"ID\",\"hasVideo\":true,\"t\":18,\"d\":40,\"playing\":false}");
        Assert.False(clock.IsPlaying);
        Assert.Equal(18, clock.GetCurrentTimeSeconds());
        Assert.True(clock.GetVideoRect().IsEmpty);
        Send("{\"ccpVideoClock\":\"ID\",\"hasVideo\":false}");
        Assert.Equal(0, clock.GetDurationSeconds());
        Assert.Equal(0, clock.GetCurrentTimeSeconds());
        clock.Detach();
        Send("{\"ccpVideoClock\":\"ID\",\"hasVideo\":true,\"t\":20,\"playing\":true}");
        Assert.False(clock.IsPlaying);
        Assert.Equal(0, clock.GetCurrentTimeSeconds());
    });

    [Fact]
    public void ControlsSendScriptsToThePage() => AvaloniaTestDispatcher.Run(() =>
    {
        var web = new WebHost();
        var scripts = new List<string>();
        web.ScriptInvoker = js => { scripts.Add(js); return Task.FromResult<string?>(null); };
        using var clock = new WebHostVideoClock(web);
        clock.Seek(3.5); clock.Pause(); clock.Play();
        Assert.Contains(scripts, s => s.Contains("v.currentTime=3.5"));
        Assert.Contains(scripts, s => s.Contains("v.pause()"));
        Assert.Contains(scripts, s => s.Contains("v.play()"));
    });
}
