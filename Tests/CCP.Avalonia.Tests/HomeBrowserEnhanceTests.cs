using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class HomeBrowserEnhanceTests
{
    [Fact]
    public void PollReadsVideoAndDetachStopsRequests() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>("{\"t\":12.5,\"d\":30,\"paused\":false,\"rx\":0.1,\"ry\":0.2,\"rw\":0.8,\"rh\":0.7}");
        }};
        using var source = new WebHostVideoTimeSource(host);
        double? notified = null;
        source.PlaybackTimeChanged += time => notified = time;
        source.Attach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Equal(12.5, notified);
        Assert.Equal(30, source.GetDurationSeconds());
        Assert.True(source.IsPlaying);
        source.Detach();
        source.PollAsync().GetAwaiter().GetResult();
        Assert.Single(scripts);
    });
    [Fact]
    public void PlaybackCommandsUsePageVideo() => AvaloniaTestDispatcher.Run(() =>
    {
        var scripts = new List<string>();
        var host = new WebHost { ScriptInvoker = script =>
        {
            scripts.Add(script);
            return Task.FromResult<string?>(null);
        }};
        using var source = new WebHostVideoTimeSource(host);
        source.Seek(2.5);
        source.Pause();
        source.Play();
        Assert.Contains("currentTime=2.5", scripts[0]);
        Assert.Contains("v.pause()", scripts[1]);
        Assert.Contains("v.play()", scripts[2]);
    });
}
