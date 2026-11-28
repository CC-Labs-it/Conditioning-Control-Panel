using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosWebViewHost.cs:1581-1600 and :1374-1389 (#938): a hosted local page gets the setSinkId
/// script before its first navigation when the user picked an output device, and only its own origin may
/// run the microphone-label probe.</summary>
public sealed class HostedPageSinkTests
{
    [Fact]
    public Task ChosenDeviceInstallsTheSinkScriptBeforeNavigationOnEachEngine() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            service.Current.AudioOutputDeviceId = "{headphones}";
            service.Current.AudioOutputDeviceName = "Headphones \"USB\"";
            var host = new WebHost { HostedPageSink = true };
            var calls = new List<string>();
            var pending = new TaskCompletionSource<bool>();
            host.SinkScriptInstaller = script => { calls.Add(script); return pending.Task; };
            host.NewScrolllerEngine()(true);
            Assert.Null(host.ScrolllerGate);                      // the page waits for the script
            Assert.Contains("var WANTED = \"Headphones \\u0022USB\\u0022\";", Assert.Single(calls));
            pending.SetResult(true);
            await Task.Yield();
            Assert.True(host.ScrolllerGate);
            host.NewScrolllerEngine()(true);                     // a rebuilt engine gets it again
            Assert.Equal(2, calls.Count);

            var remote = new WebHost();                          // a host that is not a local page
            remote.SinkScriptInstaller = script => { calls.Add(script); return Task.FromResult(true); };
            remote.NewScrolllerEngine()(true);
            service.Current.AudioOutputDeviceId = "";            // System default: no routing
            var plain = new WebHost { HostedPageSink = true };
            plain.SinkScriptInstaller = script => { calls.Add(script); return Task.FromResult(true); };
            plain.NewScrolllerEngine()(true);
            Assert.Equal(2, calls.Count);
            Assert.True(remote.ScrolllerGate);
            Assert.True(plain.ScrolllerGate);
        }
        finally { CoreSettings.ServiceProvider = previous; }
    });

    [Fact]
    public Task LocalGamesAndIntakeRouteButJustDropDoesNot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        Assert.True(new GameWindow(GameWindow.Games[GameWindow.DtrhId]).Web.HostedPageSink);
        Assert.False(new GameWindow(GameWindow.Games[GameWindow.JustDropId]).Web.HostedPageSink);
        Assert.True(new IntakeHostWindow().Web.HostedPageSink);
        return Task.CompletedTask;
    });

    [Fact]
    public void OnlyTheAssetOriginMayProbeTheMicrophone()
    {
        using var server = new WebAssetServer(AppContext.BaseDirectory);
        Assert.True(LocalPagePolicy.AllowsMicrophone(server.Url("arcademy/index.html"), server));
        Assert.False(LocalPagePolicy.AllowsMicrophone("https://justdrop.example/shop", server));
        Assert.False(LocalPagePolicy.AllowsMicrophone(server.RelayOrigin + "/", server));
        Assert.False(LocalPagePolicy.AllowsMicrophone("not a url", server));
        Assert.False(LocalPagePolicy.AllowsMicrophone(null, server));
    }
}
