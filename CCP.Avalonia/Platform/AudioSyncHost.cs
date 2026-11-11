using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // WPF AudioSyncService.cs:137; browser transport is wired separately.
    internal static class AudioSyncHost
    {
        internal static BrowserAudioSyncService? Service { get; private set; }

        internal static void Start(HapticService haptics, AudioSyncSettings settings) =>
            Service ??= new BrowserAudioSyncService(haptics, settings, VlcAudioRangeReader.Read);

        internal static void Stop()
        {
            Service?.Dispose();
            Service = null;
        }
    }
}
