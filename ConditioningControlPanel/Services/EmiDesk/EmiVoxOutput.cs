using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>The WPF audio boundary. A missing service stays silent, as before the Core move.</summary>
internal sealed class EmiVoxOutput : IEmiVoxOutput
{
    public bool IsOutputSuppressed => App.Audio is not { } audio || audio.IsOutputSuppressed;
    public int MasterVolume => App.Settings?.Current?.MasterVolume ?? 0;
    public Action? PlayOneShot(string path, float volume, string tag)
    {
        var handle = App.Audio?.PlayOneShot(path, volume, tag);
        return handle == null ? null : handle.Stop;
    }
}
