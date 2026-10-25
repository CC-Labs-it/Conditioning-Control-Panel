using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed class BrowserAudioSyncHostTests
{
    internal static async Task<ConditioningControlPanel.Services.HapticService> MockToyAsync()
    {
        var settings = new ConditioningControlPanel.Models.HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        foreach (var provider in new[] { "lovense", "buttplug" }) settings.V2.Provider(provider).Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        Assert.True(await haptics.ConnectAsync());
        return haptics;
    }

    [Fact]
    public async Task HomeBridgeUnblocksMissingServiceAndPanicDisarmsIt() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var old = CoreHaptics.Service;
        CoreHaptics.Service = null;
        var scripts = new List<string>();
        using var host = new BrowserAudioSyncHost(new WebHost(), s => { scripts.Add(s); return Task.FromResult<string?>(null); });
        try
        {
            await host.HandleAsync("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://example.invalid/a.mp4\"}");
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
            PanicSurfaces.All.Single(s => s.Id == "browser-audio-sync").Stop(null);
            scripts.Clear();
            await host.HandleAsync("{\"type\":\"audioSyncVideoDetected\"}");
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Empty(scripts);
        }
        finally { CoreHaptics.Service = old; }
    });

    [Fact]
    public async Task NavigationInjectsOnlyOnHypnoTubeWithSyncOnAndAToyConnected() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (old, oldEnabled) = (CoreHaptics.Service, CoreSettings.Current.Haptics.AudioSync.Enabled);
        using var haptics = await MockToyAsync();
        var scripts = new List<string>();
        var web = new WebHost();
        using var host = new BrowserAudioSyncHost(web, s => { scripts.Add(s); return Task.FromResult<string?>(null); });
        try
        {
            CoreHaptics.Service = haptics;
            CoreSettings.Current.Haptics.AudioSync.Enabled = false;
            web.OnNavigationCompleted(new Uri("https://hypnotube.com/video/1"));
            CoreSettings.Current.Haptics.AudioSync.Enabled = true;
            web.OnNavigationCompleted(new Uri("http://127.0.0.1/"));
            CoreHaptics.Service = null;
            web.OnNavigationCompleted(new Uri("https://hypnotube.com/video/1"));
            Assert.Empty(scripts);
            CoreHaptics.Service = haptics;
            web.OnNavigationCompleted(new Uri("https://hypnotube.com/video/1"));
            Assert.Single(scripts);
            Assert.Contains("audioSyncVideoDetected", scripts[0]);
        }
        finally { (CoreHaptics.Service, CoreSettings.Current.Haptics.AudioSync.Enabled) = (old, oldEnabled); }
    });

    [Fact]
    public void LinuxDecoderReadsOnlyRequestedRangeAsMono44100()
    {
        // The decoder is the distro's ffmpeg; CI runners without it have nothing to exercise.
        if (!OperatingSystem.IsLinux() || !(Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
                .Any(d => File.Exists(Path.Combine(d, "ffmpeg")))) return;
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(file)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + 88200); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
                writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(88200);
                for (var i = 0; i < 44100; i++) writer.Write((short)(i < 22050 ? 0 : 16000));
            }
            var samples = BrowserAudioRangeReader.Read(file, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Equal(22050, samples.Length);
            Assert.All(samples, v => Assert.InRange(v, .48f, .50f));
        }
        finally { File.Delete(file); }
    }
}
