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
    public async Task NavigationInjectsWithoutPausingTheNewDocument() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        var scripts = new List<string>();
        var web = new WebHost();
        using var host = new BrowserAudioSyncHost(web, s => { scripts.Add(s); return Task.FromResult<string?>(null); });
        web.OnNavigationCompleted(new Uri("http://127.0.0.1/"));
        Assert.Single(scripts);
        Assert.Contains("audioSyncVideoDetected", scripts[0]);
        return Task.CompletedTask;
    });

    [Fact]
    public void LinuxDecoderReadsOnlyRequestedRangeAsMono44100()
    {
        if (!OperatingSystem.IsLinux()) return;
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
