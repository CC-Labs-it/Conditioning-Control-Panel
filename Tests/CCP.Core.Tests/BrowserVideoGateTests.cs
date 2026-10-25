using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The browser video gate's rules, moved to Core from the WPF head: the container list,
/// the engine switch and the #938 device pin, with the head's engine half behind
/// <see cref="BrowserVideoGate.EngineAccepts"/>.</summary>
[Collection(SessionStatics.Name)]
public sealed class BrowserVideoGateTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly Func<string, string?, bool>? _engine = BrowserVideoGate.EngineAccepts;
    private readonly SettingsService _service = new();
    private readonly List<(string Path, string? Key)> _asked = new();

    public BrowserVideoGateTests()
    {
        CoreSettings.ServiceProvider = () => _service;
        _service.Current.BrowserVideoEngineEnabled = true;
        _service.Current.AudioOutputDeviceId = "";
        BrowserVideoGate.EngineAccepts = (path, key) => { _asked.Add((path, key)); return true; };
    }

    public void Dispose()
    {
        CoreSettings.ServiceProvider = _provider;
        BrowserVideoGate.EngineAccepts = _engine;
    }

    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("CLIP.M4V", true)]
    [InlineData(@"C:\v\clip.WebM", true)]
    [InlineData("clip.mkv", false)]
    [InlineData("clip.avi", false)]
    [InlineData("clip.mov", false)]
    [InlineData("clip", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyChromiumContainers_AreBrowserExtensions(string? path, bool expected)
        => Assert.Equal(expected, BrowserVideoGate.IsBrowserExtension(path));

    [Fact]
    public void EnabledMp4_WithAnAcceptingEngine_UsesTheBrowser_AndPassesTheKeyThrough()
    {
        Assert.True(BrowserVideoGate.ShouldUseBrowser("clip.mp4", "pack:abc"));
        Assert.Equal(new[] { ("clip.mp4", (string?)"pack:abc") }, _asked);
    }

    [Fact]
    public void SettingsNotLoaded_RoutesToLibVlc_WithoutAskingTheEngine()
    {
        CoreSettings.ServiceProvider = () => null;
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
        Assert.Empty(_asked);
    }

    [Fact]
    public void NoEngineSeeded_RoutesToLibVlc()
    {
        BrowserVideoGate.EngineAccepts = null;
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
    }

    [Fact]
    public void EngineDeclines_RoutesToLibVlc()
    {
        BrowserVideoGate.EngineAccepts = (_, _) => false;
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
    }

    [Fact]
    public void EngineSwitchedOff_RoutesToLibVlc_WithoutAskingTheEngine()
    {
        _service.Current.BrowserVideoEngineEnabled = false;
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
        Assert.Empty(_asked);
    }

    [Fact]
    public void APinnedAudioDevice_RoutesToLibVlc_938()
    {
        _service.Current.AudioOutputDeviceId = "{0.0.0.00000000}.{headphones}";
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
        Assert.Empty(_asked);
    }

    [Theory]
    [InlineData("clip.mkv")]
    [InlineData("")]
    [InlineData(null)]
    public void NonBrowserPaths_NeverReachTheEngine(string? path)
    {
        Assert.False(BrowserVideoGate.ShouldUseBrowser(path));
        Assert.Empty(_asked);
    }

    [Fact]
    public void AThrowingEngine_RoutesToLibVlc_InsteadOfThrowing()
    {
        BrowserVideoGate.EngineAccepts = (_, _) => throw new InvalidOperationException("webview2 gone");
        Assert.False(BrowserVideoGate.ShouldUseBrowser("clip.mp4"));
    }
}
