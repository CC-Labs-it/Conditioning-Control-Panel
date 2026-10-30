using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF AudioService.EnumerateOutputDevices: active WASAPI render endpoints only.</summary>
internal static class WindowsAudioDevices
{
    [SupportedOSPlatform("windows")]
    internal static List<LibVlcAudio.OutputDevice> Enumerate(List<LibVlcAudio.OutputDevice> list)
        => Append(list, ReadActive);

    internal static List<LibVlcAudio.OutputDevice> Append(List<LibVlcAudio.OutputDevice> list,
        Func<IEnumerable<LibVlcAudio.OutputDevice>> read)
    {
        try { foreach (var device in read()) list.Add(device); }
        catch (Exception ex) { Log.Information(ex, "Audio: playback endpoints unavailable"); }
        return list;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<LibVlcAudio.OutputDevice> ReadActive()
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                LibVlcAudio.OutputDevice? entry = null;
                try { entry = new(device.ID ?? "", device.FriendlyName ?? "(unnamed)"); }
                catch (Exception ex) { Log.Debug(ex, "Audio: unreadable playback endpoint"); }
                if (entry != null) yield return entry;
            }
        }
    }
}
