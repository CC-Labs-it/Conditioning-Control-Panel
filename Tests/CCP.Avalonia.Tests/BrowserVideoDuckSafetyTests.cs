using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserVideoDuckSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsSweepAndRescanProtectBrowserVideo(bool active)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGate = BrowserVideoGate.EngineAccepts;
        var settings = new SettingsService();
        var owner = new object();
        var volumes = new Dictionary<int, float> { [30] = 1f };
        var dir = Directory.CreateTempSubdirectory("ccp-browser-duck").FullName;
        var ducker = new WindowsDucker(body => body(volumes.Keys.Select(pid =>
            new DuckSession(pid, () => volumes[pid], value => volumes[pid] = value)).ToList()),
            _ => "msedgewebview2", dir, () => false, 999);
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.MasterVolume = 100;
            settings.Current.ExcludeBambiCloudFromDucking = false;
            settings.Current.BrowserVideoEngineEnabled = true;
            settings.Current.AudioOutputDeviceId = "";
            BrowserVideoGate.EngineAccepts = (_, _) => true;
            if (active) Assert.True(BrowserVideoSurface.TryAcquire(owner, "sample.mp4"));
            ducker.Duck(50); ducker.Drain();
            Assert.Equal(active ? 1f : 0.5f, volumes[30], 3);
            volumes[31] = 1f;
            ducker.Rescan(); ducker.Drain();
            Assert.Equal(active ? 1f : 0.5f, volumes[31], 3);
        }
        finally
        {
            ducker.ForceUnduck(); ducker.Drain(); BrowserVideoSurface.Release(owner);
            settings.SealForReset(); CoreSettings.ServiceProvider = oldSettings; BrowserVideoGate.EngineAccepts = oldGate;
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxSweepAndRescanProtectBrowserVideo(bool active)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGate = BrowserVideoGate.EngineAccepts;
        var settings = new SettingsService();
        var owner = new object();
        var clock = new AudioDuckRescanTests.Clock();
        var sets = new List<string>();
        var count = 2;
        string Streams() => JsonSerializer.Serialize(Enumerable.Range(1, count).Select(i => new {
            index = i, volume = new { left = new { value = 65536 } },
            properties = new Dictionary<string, string> { ["application.process.id"] = i.ToString(),
                ["application.process.binary"] = i % 2 == 1 ? "WebKitWebProcess" : "msedgewebview2" }
        }));
        var audio = new LibVlcAudio(args => { if (args.StartsWith("-f json")) return Streams(); sets.Add(args); return ""; }, clock);
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.MasterVolume = 100;
            settings.Current.ExcludeBambiCloudFromDucking = false;
            settings.Current.BrowserVideoEngineEnabled = true;
            settings.Current.AudioOutputDeviceId = "";
            BrowserVideoGate.EngineAccepts = (_, _) => true;
            if (active) Assert.True(BrowserVideoSurface.TryAcquire(owner, "sample.mp4"));
            audio.Duck(50); audio.Drain();
            Assert.Equal(active ? 0 : 2, sets.Count);
            count = 4; clock.Advance(TimeSpan.FromMilliseconds(2500)); audio.Drain();
            Assert.Equal(active ? 0 : 4, sets.Count);
        }
        finally
        {
            audio.Shutdown(); BrowserVideoSurface.Release(owner);
            settings.SealForReset(); CoreSettings.ServiceProvider = oldSettings; BrowserVideoGate.EngineAccepts = oldGate;
        }
    }
}
